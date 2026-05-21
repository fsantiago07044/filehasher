using WixToolset.Dtf.WindowsInstaller;

namespace FileHasher.Tests;

/// <summary>
/// Test helper that produces purpose-built adversarial MSI files by copying a
/// known-good template (the existing <c>fixtures/msi-test.msi</c>) into a temp
/// location and mutating specific rows of the MSI database in place. Each
/// instance owns exactly one temp MSI; the file is deleted on Dispose.
///
/// The mutations available are:
///   • <see cref="SetFirstFileName"/> — overwrite the FileName column of the
///     File row whose Sequence == 1, used to inject path-traversal payloads
///     (e.g. <c>"..\..\evil.exe"</c>) and verify the extractor's post-extraction
///     guards do not let them escape.
///   • <see cref="SetFirstFileSize"/> — overwrite the FileSize column of the
///     same row, used to trip the per-file size cap from real adversarial
///     input (rather than from an absurdly-tight constructor parameter).
///   • <see cref="InjectDirectoryTraversal"/> — overwrite a child Directory
///     row's DefaultDir column with a <c>"..\..\"</c> payload.
///   • <see cref="InsertSyntheticFileRows"/> — append N File rows so the
///     declared file count exceeds the count cap.
///
/// Every mutation opens the MSI in <see cref="DatabaseOpenMode.Direct"/>, makes
/// its change through the documented View/Record pattern, commits, and closes
/// before returning. Tests can therefore chain mutations without worrying
/// about ordering or transaction overlap.
/// </summary>
internal sealed class MaliciousMsiBuilder : IDisposable
{
    public string Path { get; }

    public MaliciousMsiBuilder(string templatePath)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Template MSI not found.", templatePath);

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FileHasherTest_malicious_" + System.IO.Path.GetRandomFileName() + ".msi");
        File.Copy(templatePath, Path);
    }

    /// <summary>
    /// Overwrites the FileName column of the File row whose Sequence == 1.
    /// The value is taken literally and not validated against MSI's "short|long"
    /// FileName format, so callers can pass adversarial inputs like
    /// "..\..\evil.exe" to probe the extractor.
    /// </summary>
    public MaliciousMsiBuilder SetFirstFileName(string newFileName)
    {
        using var db = new Database(Path, DatabaseOpenMode.Direct);
        using var view = db.OpenView("SELECT `FileName` FROM `File` WHERE `Sequence` = 1");
        view.Execute();
        using var record = view.Fetch()
            ?? throw new InvalidOperationException("Template MSI has no File row with Sequence = 1.");
        record.SetString(1, newFileName);
        view.Modify(ViewModifyMode.Update, record);
        db.Commit();
        return this;
    }

    /// <summary>
    /// Overwrites the FileSize column of the File row whose Sequence == 1
    /// with the given value. MSI's FileSize is a DoubleInteger (32-bit
    /// signed), so values up to <see cref="int.MaxValue"/> (~2 GiB) can be
    /// written directly; larger inputs are clamped.
    /// </summary>
    public MaliciousMsiBuilder SetFirstFileSize(long newSize)
    {
        using var db = new Database(Path, DatabaseOpenMode.Direct);
        using var view = db.OpenView("SELECT `FileSize` FROM `File` WHERE `Sequence` = 1");
        view.Execute();
        using var record = view.Fetch()
            ?? throw new InvalidOperationException("Template MSI has no File row with Sequence = 1.");
        record.SetInteger(1, (int)Math.Min(newSize, int.MaxValue));
        view.Modify(ViewModifyMode.Update, record);
        db.Commit();
        return this;
    }

    /// <summary>
    /// Overwrites the DefaultDir column of the first non-root Directory row
    /// with a path-traversal payload (<c>"..\..\evil_dir"</c>). Used to test
    /// that the extractor's post-extraction guard catches escapes via the
    /// Directory tree, not just via FileName.
    /// </summary>
    public MaliciousMsiBuilder InjectDirectoryTraversal()
    {
        using var db = new Database(Path, DatabaseOpenMode.Direct);
        using var view = db.OpenView(
            "SELECT `DefaultDir` FROM `Directory` WHERE `Directory_Parent` IS NOT NULL");
        view.Execute();
        using var record = view.Fetch()
            ?? throw new InvalidOperationException("Template MSI has no child Directory row.");
        record.SetString(1, @"..\..\evil_dir");
        view.Modify(ViewModifyMode.Update, record);
        db.Commit();
        return this;
    }

    /// <summary>
    /// Inserts <paramref name="additionalCount"/> synthetic File rows. The new
    /// rows reference an existing Component (so foreign-key constraints are
    /// satisfied even though we never actually extract these files; the cap
    /// check in <see cref="MsiExtractor.ExtractAsync"/> aborts before extraction
    /// touches disk). File and FileName primary keys are namespaced with a
    /// <c>_synth_</c> prefix so they cannot collide with template rows.
    /// </summary>
    public MaliciousMsiBuilder InsertSyntheticFileRows(int additionalCount)
    {
        if (additionalCount <= 0) return this;

        using var db = new Database(Path, DatabaseOpenMode.Direct);

        // Crib an existing Component_ value to use as the FK target for the new rows.
        string firstComponent;
        using (var compView = db.OpenView("SELECT `Component_` FROM `File`"))
        {
            compView.Execute();
            using var compRecord = compView.Fetch()
                ?? throw new InvalidOperationException("Template MSI has no File rows at all.");
            firstComponent = compRecord.GetString(1);
        }

        // Use the highest existing Sequence as the base so we don't collide.
        int seqBase = 10_000;
        using (var seqView = db.OpenView("SELECT `Sequence` FROM `File`"))
        {
            seqView.Execute();
            while (true)
            {
                using var seqRecord = seqView.Fetch();
                if (seqRecord == null) break;
                var s = seqRecord.GetInteger(1);
                if (s >= seqBase) seqBase = s + 1;
            }
        }

        using var insertView = db.OpenView(
            "SELECT `File`, `Component_`, `FileName`, `FileSize`, `Attributes`, `Sequence` FROM `File`");
        insertView.Execute();
        for (int i = 0; i < additionalCount; i++)
        {
            using var rec = new Record(6);
            rec.SetString(1, $"_synth_{i}");
            rec.SetString(2, firstComponent);
            rec.SetString(3, $"synth_{i}.dat");
            rec.SetInteger(4, 1);
            rec.SetInteger(5, 0);
            rec.SetInteger(6, seqBase + i);
            insertView.Modify(ViewModifyMode.Insert, rec);
        }
        db.Commit();
        return this;
    }

    public void Dispose()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { /* best-effort */ }
    }
}
