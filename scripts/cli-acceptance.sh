#!/usr/bin/env bash
# FileHasher CLI acceptance run. Drives a published binary from outside, so it
# needs no .NET on the machine running it; that is the point, since the Linux
# VMs this is aimed at have no SDK.
#
#   FH_BIN=/path/to/filehasher ./cli-acceptance.sh
#
# It lives in the repo because it has already caught something the unit tests
# could not: the symlink guard compared two paths that resolved different
# amounts, which only shows up when the scan path sits behind a symlinked
# ancestor. The xunit fixtures build under Path.GetTempPath(), which on the
# machines we run them on is already canonical, so they all passed.
#   ./run-tests.sh            runs everything, prints PASS/FAIL per check
# Exit 0 only if every check passed.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN="${FH_BIN:-$HERE/bin/filehasher}"   # the test kit ships the binary at bin/; otherwise set FH_BIN
W="$(mktemp -d)"; trap 'rm -rf "$W"' EXIT
pass=0; fail=0
ok()   { printf '  PASS  %s\n' "$1"; pass=$((pass+1)); }
bad()  { printf '  FAIL  %s\n     %s\n' "$1" "$2"; fail=$((fail+1)); }
chk()  { # chk "name" expected_exit cmd...
  local name="$1" want="$2"; shift 2
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ]; then ok "$name"; else bad "$name" "expected exit $want, got $got: $(echo "$out" | head -2 | tr '\n' ' ')"; fi
}

echo "== environment =="
echo "  $(uname -srm)"
echo "  $( (. /etc/os-release 2>/dev/null && echo "$PRETTY_NAME") || echo 'unknown distro')"
chmod +x "$BIN" 2>/dev/null
echo "  filehasher $("$BIN" --version 2>&1)"
echo

echo "== 1. the engine runs at all =="
mkdir -p "$W/t/sub"; printf 'alpha\n' > "$W/t/a.txt"; printf 'beta\n' > "$W/t/sub/b.txt"
chk "hash a folder"                 0 "$BIN" hash "$W/t" --all-types -q
chk "write sidecars"                0 "$BIN" hash "$W/t" --all-types --sidecar -q
[ -f "$W/t/a.txt.sha256" ] && ok "sidecar file created" || bad "sidecar file created" "missing"
chk "verify them back"              0 "$BIN" verify "$W/t" --all-types -q

echo
echo "== 2. it detects corruption and fails the build =="
printf 'tampered\n' > "$W/t/sub/b.txt"
chk "tamper returns exit 1"         1 "$BIN" verify "$W/t" --all-types -q
chk "--fail-on none reports only"   0 "$BIN" verify "$W/t" --all-types -q --fail-on none

echo
echo "== 3. cross-platform interop: sidecars written on macOS =="
if [ -d "$HERE/macos-written" ]; then
  cp -r "$HERE/macos-written" "$W/mac"
  chk "macOS sidecars verify on Linux" 0 "$BIN" verify "$W/mac" --all-types -q
else
  echo "  SKIP  no macos-written/ fixture beside this script"
fi

echo
echo "== 4. symlink loop must not hang =="
mkdir -p "$W/loop/data/sub"; printf 'x\n' > "$W/loop/data/f.txt"; printf 'y\n' > "$W/loop/data/sub/g.txt"
ln -sfn "$W/loop/data"     "$W/loop/data/self"    # points at its own ancestor
ln -sfn "$W/loop/data/sub" "$W/loop/data/alias"   # NOT a loop: must still be followed
# timeout is coreutils and is not on every system this script gets tried
# on; without it the check still runs, it just cannot report a hang as a
# hang rather than as the shell hanging.
if command -v timeout >/dev/null; then TO="timeout 60"; else TO=""; fi
out="$($TO "$BIN" hash "$W/loop/data" --all-types 2>&1)"; rc=$?
if [ "$rc" = "124" ]; then bad "loop terminates" "HUNG, timed out after 60s"
elif echo "$out" | grep -q "loops back to an ancestor"; then ok "loop skipped with a warning"
else bad "loop skipped with a warning" "$(echo "$out" | head -2 | tr '\n' ' ')"; fi
if [ "$(echo "$out" | grep -c 'alias/g.txt')" = "1" ]; then ok "non-loop alias still followed"
else bad "non-loop alias still followed" "alias was pruned; that is the rejected behaviour"; fi

# The same loop reached through a symlinked parent. This is what the Mac found
# and the unit tests did not: the two paths being compared resolved different
# amounts, so the guard never fired and the walk ran to the OS limit.
ln -sfn "$W/loop/data" "$W/gate"
out="$($TO "$BIN" hash "$W/gate" --all-types 2>&1)"
if echo "$out" | grep -q "loops back to an ancestor"; then ok "loop caught behind a symlinked parent"
else bad "loop caught behind a symlinked parent" "$(echo "$out" | tail -1)"; fi

echo
echo "== 5. depth control =="
n_all=$("$BIN" hash "$W/t" --all-types 2>/dev/null | grep -c '^[0-9A-F]\{64\}')
n_top=$("$BIN" hash "$W/t" --all-types --max-depth 0 2>/dev/null | grep -c '^[0-9A-F]\{64\}')
[ "$n_all" -gt "$n_top" ] && ok "--max-depth 0 narrows the walk ($n_all then $n_top)" \
                          || bad "--max-depth 0 narrows the walk" "got $n_all and $n_top"
chk "--no-recurse accepted"         0 "$BIN" hash "$W/t" --all-types -q --no-recurse
chk "-r accepted as a no-op"        0 "$BIN" hash "$W/t" --all-types -q -r

echo
echo "== 6. machine-readable output =="
if "$BIN" hash "$W/t" --all-types --max-depth 0 --json 2>/dev/null | head -1 | grep -q '^{"type":"file"'; then
  ok "--json emits one object per line"; else bad "--json emits one object per line" "unexpected shape"; fi
if "$BIN" hash "$W/t" --all-types --max-depth 0 --json 2>/dev/null | tail -1 | grep -q '"type":"summary"'; then
  ok "--json ends with a summary"; else bad "--json ends with a summary" "missing"; fi

echo
echo "== 7. usage errors are distinguishable from findings =="
chk "missing path exits 2"          2 "$BIN" hash "$W/does-not-exist"
chk "bad algorithm exits 2"         2 "$BIN" hash "$W/t" -a sha999
chk "unknown option exits 2"        2 "$BIN" hash "$W/t" --nope
chk "--msi-inner refused off Windows" 2 "$BIN" hash "$W/t" --msi-inner

echo
echo "== 8. the file-type filter =="
mkdir -p "$W/types"
for f in app.deb app.rpm tool.AppImage src.tar.gz plain.gz notes.txt; do printf 'x\n' > "$W/types/$f"; done
n() { "$BIN" hash "$W/types" "$@" 2>/dev/null | grep -c '^[0-9A-F]\{64\}'; }
[ "$(n --all-types)" = "6" ] && ok "--all-types takes every file" \
                             || bad "--all-types takes every file" "got $(n --all-types)"
[ "$(n --types deb,rpm)" = "2" ] && ok "--types deb,rpm takes two" \
                                 || bad "--types deb,rpm takes two" "got $(n --types deb,rpm)"
# The whole reason matching looks at the end of the name: ".gz" alone would
# also catch plain.gz, and a person typing tar.gz means the pair.
out="$("$BIN" hash "$W/types" --types tar.gz 2>/dev/null)"
if [ "$(echo "$out" | grep -c 'src.tar.gz')" = "1" ] && ! echo "$out" | grep -q 'plain.gz$'; then
  ok "--types tar.gz does not match plain.gz"
else bad "--types tar.gz does not match plain.gz" "$(echo "$out" | tr '\n' ' ')"; fi
[ "$(n --types ' *.APPIMAGE ;.DEB ')" = "2" ] && ok "separators, dots and case are forgiving" \
    || bad "separators, dots and case are forgiving" "got $(n --types ' *.APPIMAGE ;.DEB ')"
# A verify filtered differently from the run that wrote the sidecars reports
# NO SIDECAR for files the run never looked at.
"$BIN" hash "$W/types" --types deb,rpm --sidecar -q >/dev/null 2>&1
chk "verify with the same list is clean" 0 "$BIN" verify "$W/types" --types deb,rpm -q
# Note the default --fail-on for verify does not include no-sidecar, so this
# is about what gets reported, not the exit code: a verify run wider than the
# hash run invents NO SIDECAR rows for the four files it never hashed.
gap="$("$BIN" verify "$W/types" --all-types 2>/dev/null | grep -c 'NO SIDECAR')"
[ "$gap" = "4" ] && ok "a wider verify reports the four it never hashed" \
                 || bad "a wider verify reports the four it never hashed" "got $gap NO SIDECAR rows"
chk "--fail-on no-sidecar turns that into exit 1" 1 "$BIN" verify "$W/types" --all-types -q --fail-on no-sidecar

echo
echo "== 9. things that are not files =="
mkdir -p "$W/special"; printf 'x\n' > "$W/special/real.txt"; : > "$W/special/empty.txt"
mkfifo "$W/special/pipe" 2>/dev/null
if [ -p "$W/special/pipe" ]; then
  # A pipe with no writer blocks forever on open, so this used to wedge the
  # whole run with no error and no progress. /tmp is full of them.
  out="$($TO "$BIN" hash "$W/special" --all-types 2>&1)"; rc=$?
  if [ "$rc" = "124" ]; then bad "a pipe does not hang the run" "HUNG, timed out"
  elif echo "$out" | grep -q "not a regular file"; then ok "a pipe is skipped with a warning"
  else bad "a pipe is skipped with a warning" "$(echo "$out" | head -2 | tr '\n' ' ')"; fi
  [ "$(echo "$out" | grep -c '^[0-9A-F]\{64\}')" = "2" ] && ok "the other two files still hashed" \
      || bad "the other two files still hashed" "got $(echo "$out" | grep -c '^[0-9A-F]\{64\}')"
else
  echo "  SKIP  mkfifo unavailable"
fi

echo
echo "== 10. a large-ish tree, for timing =="
mkdir -p "$W/big"; for i in $(seq 1 500); do printf 'content %s\n' "$i" > "$W/big/f$i.dat"; done
s=$(date +%s); "$BIN" hash "$W/big" --all-types -q >/dev/null 2>&1; e=$(date +%s)
ok "500 files hashed in $((e-s))s"

echo
echo "=============================="
printf '  %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" = "0" ] && echo "  ALL GOOD" || echo "  SOMETHING FAILED, send me the output"
exit $([ "$fail" = "0" ] && echo 0 || echo 1)
