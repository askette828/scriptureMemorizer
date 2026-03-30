using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TextMemorizer;

public partial class Form1 : Form
{
    private readonly Color _backgroundColor = Color.FromArgb(246, 248, 251);
    private readonly Color _surfaceColor = Color.White;
    private readonly Color _textColor = Color.FromArgb(32, 35, 42);
    private readonly Color _mutedTextColor = Color.FromArgb(135, 140, 150);
    private readonly Color _accentColor = Color.FromArgb(45, 116, 220);
    private readonly Color _accentHoverColor = Color.FromArgb(65, 135, 235);

    private RichTextBox _textBox = null!;
    private Button _nextButton = null!;
    private Button _resetButton = null!;
    private Label _stepLabel = null!;
    private ProgressBar _progressBar = null!;
    private RoundedPanel _textHost = null!;
    private System.Windows.Forms.Timer _fadeTimer = null!;
    private string _pendingText = string.Empty;
    private string _pendingDimText = string.Empty;
    private List<(int start, int length)> _pendingRanges = new();

    private string _originalText = string.Empty;
    private List<string> _tokens = new();
    private List<int> _wordTokenIndexes = new();
    private List<int> _shuffledWordTokenIndexes = new();
    private int _step = 1;
    private bool _isStarted;

    private static readonly double[] StepHideRatios = { 0.0, 0.25, 0.45, 0.7, 0.9 };
    private static readonly double[] LetterHideRatios = { 0.0, 0.45, 0.65, 0.85, 1.0 };

    public Form1()
    {
        InitializeComponent();
        UpdateUiState(isStarted: false);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        Text = "Scripture Memorizer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 500);
        BackColor = _backgroundColor;
        ForeColor = _textColor;
        Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);

        _textBox = new RichTextBox
        {
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = _surfaceColor,
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point)
        };

        _textHost = new RoundedPanel(14, _surfaceColor, Color.FromArgb(18, 30, 50, 80))
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14)
        };
        _textHost.Controls.Add(_textBox);

        _stepLabel = new Label
        {
            Text = "Step: 1",
            AutoSize = true,
            ForeColor = _mutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _progressBar = new ProgressBar
        {
            Minimum = 1,
            Maximum = 5,
            Value = 1,
            Height = 8,
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Continuous
        };

        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressLayout.Controls.Add(_stepLabel, 0, 0);
        progressLayout.Controls.Add(_progressBar, 1, 0);

        _nextButton = CreateAccentButton("Next Step");
        _nextButton.Click += NextButton_Click;

        _resetButton = CreateGhostButton("Reset");
        _resetButton.Click += ResetButton_Click;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false
        };
        buttonPanel.Controls.Add(_nextButton);
        buttonPanel.Controls.Add(_resetButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        layout.Controls.Add(_textHost, 0, 0);
        layout.Controls.Add(progressLayout, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 180 };
        _fadeTimer.Tick += FadeTimer_Tick;
    }

    private void NextButton_Click(object? sender, EventArgs e)
    {
        if (!_isStarted)
        {
            StartSession(advanceToNext: true);
            return;
        }

        if (_step >= 5)
        {
            return;
        }

        _step++;
        UpdateOutput();
        UpdateUiState(isStarted: true);
    }

    private void StartSession(bool advanceToNext)
    {
        if (string.IsNullOrWhiteSpace(_textBox.Text))
        {
            MessageBox.Show("Please enter some text to memorize.", "Text Memorizer");
            return;
        }

        _originalText = _textBox.Text;
        _step = 1;
        _isStarted = true;

        TokenizeText(_originalText);
        CreateShuffleOrder(_originalText);

        if (advanceToNext && _step < 5)
        {
            _step = 2;
        }

        UpdateOutput();
        UpdateUiState(isStarted: true);
        _textBox.ReadOnly = true;
    }

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        _originalText = string.Empty;
        _tokens.Clear();
        _wordTokenIndexes.Clear();
        _shuffledWordTokenIndexes.Clear();
        _step = 1;
        _textBox.Text = string.Empty;
        _textBox.ReadOnly = false;
        _isStarted = false;
        _fadeTimer.Stop();
        _pendingText = string.Empty;
        _pendingDimText = string.Empty;
        _pendingRanges.Clear();
        UpdateUiState(isStarted: false);
    }

    private void UpdateUiState(bool isStarted)
    {
        _nextButton.Enabled = !isStarted || _step < 5;
        _resetButton.Enabled = isStarted;
        _stepLabel.Text = $"Step: {_step}";
        _progressBar.Value = _step;
    }

    private void UpdateOutput()
    {
        if (_tokens.Count == 0 || _wordTokenIndexes.Count == 0)
        {
            _textBox.Text = _originalText;
            return;
        }

        var ratio = StepHideRatios[_step - 1];
        var hideCount = (int)Math.Round(_wordTokenIndexes.Count * ratio, MidpointRounding.AwayFromZero);
        if (_step > 1 && _wordTokenIndexes.Count > 0)
        {
            hideCount = Math.Max(1, hideCount);
        }
        hideCount = Math.Min(hideCount, _wordTokenIndexes.Count);
        var hiddenSet = new HashSet<int>(_shuffledWordTokenIndexes.Take(hideCount));
        var wordSet = new HashSet<int>(_wordTokenIndexes);

        if (hideCount == 0)
        {
            _textBox.Text = _originalText;
            return;
        }

        var dimBuilder = new StringBuilder();
        var finalBuilder = new StringBuilder();
        var ranges = new List<(int start, int length)>();

        for (var i = 0; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            var isHidden = wordSet.Contains(i) && hiddenSet.Contains(i);

            if (isHidden)
            {
                var start = dimBuilder.Length;
                dimBuilder.Append(token);
                ranges.Add((start, token.Length));
                finalBuilder.Append(GetMaskedToken(token, _step));
            }
            else
            {
                dimBuilder.Append(token);
                finalBuilder.Append(token);
            }
        }

        _pendingText = finalBuilder.ToString();
        _pendingDimText = dimBuilder.ToString();
        _pendingRanges = ranges;
        RenderDimmedText();
    }

    private void TokenizeText(string text)
    {
        _tokens.Clear();
        _wordTokenIndexes.Clear();

        var matches = Regex.Matches(text, "[A-Za-z0-9']+|[^A-Za-z0-9']+");
        for (var i = 0; i < matches.Count; i++)
        {
            var token = matches[i].Value;
            _tokens.Add(token);
            if (Regex.IsMatch(token, "^[A-Za-z0-9']+$"))
            {
                _wordTokenIndexes.Add(i);
            }
        }
    }

    private void CreateShuffleOrder(string text)
    {
        _shuffledWordTokenIndexes = new List<int>(_wordTokenIndexes);
        if (_shuffledWordTokenIndexes.Count == 0)
        {
            return;
        }

        var seed = GetStableSeed(text);
        var rng = new Random(seed);
        for (var i = _shuffledWordTokenIndexes.Count - 1; i > 0; i--)
        {
            var swapIndex = rng.Next(i + 1);
            (_shuffledWordTokenIndexes[i], _shuffledWordTokenIndexes[swapIndex]) =
                (_shuffledWordTokenIndexes[swapIndex], _shuffledWordTokenIndexes[i]);
        }
    }

    private static int GetStableSeed(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return BitConverter.ToInt32(bytes, 0);
    }

    private void RenderDimmedText()
    {
        _fadeTimer.Stop();
        _textBox.Text = string.IsNullOrEmpty(_pendingDimText) ? _originalText : _pendingDimText;

        _textBox.SelectionStart = 0;
        _textBox.SelectionLength = _textBox.TextLength;
        _textBox.SelectionColor = _textColor;

        foreach (var (start, length) in _pendingRanges)
        {
            _textBox.SelectionStart = start;
            _textBox.SelectionLength = length;
            _textBox.SelectionColor = _mutedTextColor;
        }

        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.SelectionLength = 0;
        _textBox.SelectionColor = _textColor;

        if (_pendingRanges.Count > 0)
        {
            _fadeTimer.Start();
        }
    }

    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        _fadeTimer.Stop();
        if (!string.IsNullOrEmpty(_pendingText))
        {
            _textBox.Text = _pendingText;
        }
        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.SelectionLength = 0;
        _textBox.SelectionColor = _textColor;
    }

    private static string GetMaskedToken(string token, int step)
    {
        var letterRatio = LetterHideRatios[Math.Clamp(step - 1, 0, LetterHideRatios.Length - 1)];
        if (letterRatio <= 0)
        {
            return token;
        }

        var chars = token.ToCharArray();
        var letterIndexes = new List<int>();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetterOrDigit(chars[i]))
            {
                letterIndexes.Add(i);
            }
        }

        if (letterIndexes.Count == 0)
        {
            return token;
        }

        var hideLetters = (int)Math.Ceiling(letterIndexes.Count * letterRatio);
        hideLetters = Math.Clamp(hideLetters, 1, letterIndexes.Count);

        for (var i = 0; i < hideLetters; i++)
        {
            chars[letterIndexes[i]] = '_';
        }

        return new string(chars);
    }

    private Button CreateAccentButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Height = 38,
            Width = 130,
            FlatStyle = FlatStyle.Flat,
            BackColor = _accentColor,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderSize = 0;
        button.MouseEnter += (_, _) => button.BackColor = _accentHoverColor;
        button.MouseLeave += (_, _) => button.BackColor = _accentColor;
        button.Resize += (_, _) => ApplyRoundedRegion(button, 10);
        return button;
    }

    private Button CreateGhostButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Height = 38,
            Width = 110,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(248, 250, 254),
            ForeColor = _textColor,
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderColor = Color.FromArgb(206, 214, 228);
        button.FlatAppearance.BorderSize = 1;
        button.MouseEnter += (_, _) => button.BackColor = Color.FromArgb(237, 243, 255);
        button.MouseLeave += (_, _) => button.BackColor = Color.FromArgb(248, 250, 254);
        button.Resize += (_, _) => ApplyRoundedRegion(button, 10);
        return button;
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        var rect = new Rectangle(0, 0, control.Width, control.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        control.Region = new Region(path);
    }

    private sealed class RoundedPanel : Panel
    {
        private readonly int _radius;
        private readonly Color _fillColor;
        private readonly Color _shadowColor;

        public RoundedPanel(int radius, Color fillColor, Color shadowColor)
        {
            _radius = radius;
            _fillColor = fillColor;
            _shadowColor = shadowColor;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var shadowRect = new Rectangle(4, 6, Width - 8, Height - 8);
            using var shadowPath = GetRoundedPath(shadowRect, _radius + 2);
            using var shadowBrush = new SolidBrush(_shadowColor);
            e.Graphics.FillPath(shadowBrush, shadowPath);

            var rect = new Rectangle(0, 0, Width - 8, Height - 8);
            using var path = GetRoundedPath(rect, _radius);
            using var brush = new SolidBrush(_fillColor);
            e.Graphics.FillPath(brush, path);

            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            ApplyRoundedRegion(this, _radius);
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
