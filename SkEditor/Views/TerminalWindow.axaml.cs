using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using FluentAvalonia.UI.Windowing;
using Serilog;

namespace SkEditor.Views;

public partial class TerminalWindow : AppWindow
{
    private const char EscapeChar = '\x1b';
    private const string CrSplitPattern = "(?=\r)";

    private readonly object _lock = new();
    private StreamWriter _inputWriter;
    private Process _process;

    public TerminalWindow()
    {
        InitializeComponent();
        Focusable = true;

        LoadTerminal();
        AssignEvents();

        OutputTextBox.TextArea.Caret.CaretBrush = Brushes.Transparent;

        OutputTextBox.Options.EnableHyperlinks = false;
        OutputTextBox.Options.AllowScrollBelowDocument = false;
    }

    [GeneratedRegex(CrSplitPattern)]
    private static partial Regex CrSplitter();

    private static Encoding GetTerminalEncoding()
    {
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    [MemberNotNull(nameof(_process))]
    [MemberNotNull(nameof(_inputWriter))]
    private void LoadTerminal()
    {
        InitializeProcess();
        _process.Start();

        SetupProcessHandlers();
    }

    [MemberNotNull(nameof(_process))] 
    private void InitializeProcess()
    {
        Encoding encoding = GetTerminalEncoding();

        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,

            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,

            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),

            EnvironmentVariables =
            {
                ["COLORTERM"] = "",
                ["NO_COLOR"] = "1",
                ["TERM"] = "dumb"
            }
        };

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    [MemberNotNull(nameof(_inputWriter))]
    private void SetupProcessHandlers()
    {
        Task.Run(() => ReadAsync(_process.StandardOutput));
        Task.Run(() => ReadAsync(_process.StandardError));

        _inputWriter = new StreamWriter(_process.StandardInput.BaseStream, GetTerminalEncoding())
        {
            AutoFlush = true
        };
    }

    private async Task ReadAsync(StreamReader reader)
    {
        while (!_process.HasExited)
        {
            char[] buffer = new char[1024];

            int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead <= 0)
            {
                continue;
            }

            string output = new(buffer, 0, bytesRead);

            Dispatcher.UIThread.Invoke(() => AppendText(output));
        }
    }

    private void AppendText(string text)
    {
        lock (_lock)
        {
            bool atEnd = OutputTextBox.ExtentHeight <= OutputTextBox.VerticalOffset + OutputTextBox.ViewportHeight;

            Array.ForEach(CrSplitter().Split(text), AppendPart);

            if (atEnd)
            {
                OutputTextBox.ScrollToEnd();
            }
        }
    }

    private string ProcessText(string text)
    {
        bool escapeSequence = false;
        StringBuilder builder = new();

        string rawCodes = "";
        foreach (char c in text)
        {
            if (c == EscapeChar)
            {
                escapeSequence = true;
                continue;
            }

            if (escapeSequence)
            {
                if (char.IsLetter(c))
                {
                    escapeSequence = false;

                    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                    rawCodes.Split(';').Select(int.Parse).ToArray();
                    rawCodes = "";

                    //Console.WriteLine("Found codes: " + string.Join(", ", codes) + "; end char: " + c);
                }
                else if (c != '[')
                {
                    rawCodes += c;
                }

                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private void AppendPart(string part)
    {
        part = ProcessText(part);

        TextDocument document = OutputTextBox.Document;
        DocumentLine caretLine = document.GetLineByOffset(OutputTextBox.CaretOffset);
        int offset = caretLine.Offset;
        int endOffset = caretLine.EndOffset;

        if (part.Length > 0 && part.First() == '\r')
        {
            OutputTextBox.CaretOffset = offset;
            part = part[1..];
        }

        if (part.Length > 0 && part.First() == '\n')
        {
            OutputTextBox.Document.Text += '\n';
            OutputTextBox.CaretOffset = endOffset + 1;
            part = part[1..];
        }

        if (part.Length <= 0)
        {
            return;
        }

        int caretOffset = OutputTextBox.CaretOffset;
        int lineLength = document.GetLineByOffset(caretOffset).EndOffset - caretOffset;

        document.Replace(caretOffset, Math.Min(part.Length, lineLength), part);
        OutputTextBox.CaretOffset = OutputTextBox.Text.Length;
    }

    private void AssignEvents()
    {
        InputTextBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            _inputWriter.WriteLine(InputTextBox.Text);
            InputTextBox.Text = string.Empty;
        };

        Closing += (_, _) =>
        {
            try
            {
                _inputWriter.Close();

                if (!_process.WaitForExit(1000))
                {
                    _process.Kill(true);
                }

                _process.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while closing terminal process");
            }
        };
    }
}