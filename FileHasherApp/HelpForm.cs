using System.Diagnostics;

namespace FileHasher;

/// <summary>
/// The in-app help window: topic list on the left, rendered sections on the
/// right, and a link bar (Email Support, Support Website, Privacy Policy) at
/// the bottom. Non-modal; MainForm keeps a single instance alive.
/// </summary>
internal sealed class HelpForm : Form
{
    private readonly ListBox     _topics;
    private readonly RichTextBox _content;

    private readonly Font _baseFont  = new("Segoe UI", 9.75F);
    private readonly Font _boldFont  = new("Segoe UI", 9.75F, FontStyle.Bold);
    private readonly Font _codeFont  = new("Consolas", 9.5F);
    private readonly Font _titleFont = new("Segoe UI", 14.5F, FontStyle.Bold);
    private readonly Font _headFont  = new("Segoe UI", 10.5F, FontStyle.Bold);

    public HelpForm()
    {
        Name          = "HelpForm";
        Text          = "FileHasher Help";
        Width         = 860;
        Height        = 600;
        MinimumSize   = new Size(680, 460);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = true;

        using (var iconStream = typeof(HelpForm).Assembly.GetManifestResourceStream("hash-icon.ico"))
        {
            if (iconStream != null)
                Icon = new Icon(iconStream);
        }

        // ── Bottom link bar ───────────────────────────────────────────────────

        var linkBar = new FlowLayoutPanel
        {
            Dock     = DockStyle.Bottom,
            Height   = 34,
            Padding  = new Padding(8, 6, 8, 4),
            FlowDirection = FlowDirection.LeftToRight,
        };

        linkBar.Controls.Add(MakeLink("HelpEmailLink", "Email Support",
            () => HelpContent.SupportMailto));
        linkBar.Controls.Add(MakeLink("HelpSupportSiteLink", "Support Website",
            () => HelpContent.SupportUrl));
        linkBar.Controls.Add(MakeLink("HelpPrivacyLink", "Privacy Policy",
            () => HelpContent.PrivacyUrl));

        // ── Topic list + content ──────────────────────────────────────────────

        _content = new RichTextBox
        {
            Name        = "HelpContentBox",
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            BackColor   = SystemColors.Window,
            Font        = _baseFont,
            DetectUrls  = false,
        };

        _topics = new ListBox
        {
            Name           = "HelpTopicsList",
            Dock           = DockStyle.Left,
            Width          = 225,
            IntegralHeight = false,
            Font           = new Font("Segoe UI", 9.75F),
        };
        foreach (var topic in HelpContent.Topics)
            _topics.Items.Add(topic.Title);
        _topics.SelectedIndexChanged += (_, _) => RenderSelectedTopic();

        Controls.Add(_content);
        Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4 });
        Controls.Add(_topics);
        Controls.Add(linkBar);

        _topics.SelectedIndex = 0;
    }

    private static LinkLabel MakeLink(string name, string text, Func<string> url)
    {
        var link = new LinkLabel
        {
            Name     = name,
            Text     = text,
            AutoSize = true,
            Margin   = new Padding(0, 2, 24, 0),
        };
        link.LinkClicked += (_, _) => OpenUrl(url());
        return link;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Nothing sensible to do if no browser/mail handler is registered.
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void RenderSelectedTopic()
    {
        if (_topics.SelectedIndex < 0) return;
        var topic = HelpContent.Topics[_topics.SelectedIndex];

        _content.SuspendLayout();
        _content.Clear();

        Append(topic.Title + "\n\n", _titleFont);

        foreach (var section in topic.Sections)
        {
            if (section.Heading is not null)
                Append(section.Heading + "\n", _headFont);

            foreach (var para in section.Paragraphs)
            {
                _content.SelectionIndent  = 0;
                _content.SelectionHangingIndent = 0;
                AppendInline(para);
                Append("\n\n", _baseFont);
            }

            foreach (var bullet in section.Bullets)
            {
                _content.SelectionIndent        = 10;
                _content.SelectionHangingIndent = 14;
                Append("•  ", _baseFont);
                AppendInline(bullet);
                Append("\n", _baseFont);
            }

            if (section.Bullets.Length > 0)
                Append("\n", _baseFont);
        }

        _content.SelectionStart  = 0;
        _content.SelectionLength = 0;
        _content.ScrollToCaret();
        _content.ResumeLayout();
    }

    /// <summary>Appends text, applying **bold** and `code` inline markup.</summary>
    private void AppendInline(string text)
    {
        var  i      = 0;
        var  bold   = false;
        var  code   = false;
        var  buffer = new System.Text.StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0) return;
            Append(buffer.ToString(), code ? _codeFont : (bold ? _boldFont : _baseFont));
            buffer.Clear();
        }

        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i += 2;
            }
            else if (text[i] == '`')
            {
                Flush();
                code = !code;
                i += 1;
            }
            else
            {
                buffer.Append(text[i]);
                i += 1;
            }
        }
        Flush();
    }

    private void Append(string text, Font font)
    {
        _content.SelectionStart = _content.TextLength;
        _content.SelectionFont  = font;
        _content.AppendText(text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _baseFont.Dispose();
            _boldFont.Dispose();
            _codeFont.Dispose();
            _titleFont.Dispose();
            _headFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
