using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;


namespace AP_Coursework_GUI
{
    
    public partial class MainWindow : Window
    {
        private sealed class Series
        {
            public string[] Setup { get; set; } = Array.Empty<string>();
            public string Expr { get; set; } = string.Empty;
            public string Mode { get; set; } = "linear";
            public Brush Stroke { get; set; } = Brushes.DarkBlue;
        }

        private readonly List<Series> _series = new List<Series>();
        private readonly List<(Point A, Point B, Brush Stroke)> _overlays = new List<(Point A, Point B, Brush Stroke)>();
        private readonly Brush[] _palette = new[]
        {
            (Brush)Brushes.DarkBlue, (Brush)Brushes.OrangeRed, (Brush)Brushes.ForestGreen,
            (Brush)Brushes.MediumVioletRed, (Brush)Brushes.SaddleBrown, (Brush)Brushes.DarkCyan,
            (Brush)Brushes.Goldenrod, (Brush)Brushes.SlateBlue
        };
        
        private bool _isPanning = false;
        private Point _lastPanPoint;
        private ScaleTransform _scaleTransform = new ScaleTransform(1.0, 1.0);
        private TranslateTransform _translateTransform = new TranslateTransform(0.0, 0.0);
        private TransformGroup _plotTransform;
        private const double MinZoom = 0.1;
        private const double MaxZoom = 20.0;
        private bool _livePlotEnabled = true;
        private bool _hasLive = false;
        private string _liveExpr = string.Empty;
        private string[] _liveSetup = Array.Empty<string>();
        private double _viewXmin = -10.0;
        private double _viewXmax = 10.0;
        private double _viewYmin = double.NaN;
        private double _viewYmax = double.NaN;

        private const double PlotPad = 50.0;

        public MainWindow()
        {
            InitializeComponent();
            _plotTransform = new TransformGroup();
            _plotTransform.Children.Add(_scaleTransform);
            _plotTransform.Children.Add(_translateTransform);
            this.Loaded += (s, e) =>
            {
                if (PlotCanvas != null)
                {
                    PlotCanvas.RenderTransform = _plotTransform;
                    PlotCanvas.SnapsToDevicePixels = true;
                }
            };
        }

        private string PreprocessYAssignment(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var lines = input.Split(new[] { '\n', ';' }, StringSplitOptions.None);
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var line = raw.Trim();
                var mFunc = Regex.Match(line, @"^[A-Za-z]\w*\s*\(\s*[A-Za-z]\w*\s*\)\s*=");
                var mYAssign = Regex.Match(line, @"^y\s*=\s*(.+)$", RegexOptions.IgnoreCase);
                if (!mFunc.Success && mYAssign.Success)
                {
                    string rhs = mYAssign.Groups[1].Value.Trim();
                    line = $"y(x) = {rhs}";
                }
                if (sb.Length > 0) sb.AppendLine(";");
                sb.Append(line);
            }
            return sb.ToString();
        }
        
        private void Evaluate_Click(object sender, RoutedEventArgs e)
        {

            string input = InputTextBox.Text.Trim();
            if(string.IsNullOrEmpty(input))
            {
                ErrorBox.Text = "Please enter an expression.";
                return;
            }
            try
            {
                input = PreprocessYAssignment(input);
                string result = ExprEvaluator.EvaluateExpression(input);
                if (result.StartsWith("Lexer") || result.StartsWith("Parser") ||
                   result.StartsWith("Runtime") || result.StartsWith("Error"))
                {
                    ErrorBox.Text = result;
                    ResultTextBox.Clear();
                }
                else
                {
                    ResultTextBox.Text = result;
                    ErrorBox.Clear();
                    try
                    {
                        if (ExprEvaluator.HasPlotData())
                        {
                            var data = ExprEvaluator.GetPlotData();
                            if (data != null && data.Length >= 2)
                            {
                                for (int i = 0; i + 1 < data.Length; i += 2)
                                {
                                    var p1 = new Point(data[i].Item1, data[i].Item2);
                                    var p2 = new Point(data[i + 1].Item1, data[i + 1].Item2);
                                    _overlays.Add((p1, p2, Brushes.Red));
                                }
                            }
                            try { ExprEvaluator.ClearPlotData(); } catch {}
                            
                            if (_series.Count == 0)
                            {
                                string text = InputTextBox.Text.Trim();
                                var lines = text.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                                if (lines.Length > 0)
                                {
                                    var funcDefs = lines
                                        .Select(l => Regex.Match(l, "^\\s*([A-Za-z]\\w*)\\s*\\(\\s*([A-Za-z]\\w*)\\s*\\)\\s*=").Groups)
                                        .Where(g => g.Count > 2 && !string.IsNullOrEmpty(g[1].Value))
                                        .Select(g => (name: g[1].Value.Trim(), param: g[2].Value.Trim()))
                                        .Distinct().ToList();
                                    if (funcDefs.Count > 0)
                                    {
                                        int idx = 0;
                                        foreach (var fd in funcDefs)
                                        {
                                            var srs = new Series
                                            {
                                                Setup = lines,
                                                Expr = $"{fd.name}(x)",
                                                Mode = "linear",
                                                Stroke = _palette[idx % _palette.Length]
                                            };
                                            _series.Add(srs);
                                            idx++;
                                        }
                                    }
                                    else
                                    {
                                        string expr = lines[lines.Length - 1];
                                        var srs = new Series
                                        {
                                            Setup = lines.Take(lines.Length - 1).ToArray(),
                                            Expr = expr,
                                            Mode = "linear",
                                            Stroke = _palette[_series.Count % _palette.Length]
                                        };
                                        _series.Add(srs);
                                    }
                                    // Initialize view from inputs
                                    if (!double.TryParse(XMinBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _viewXmin)) _viewXmin = -10.0;
                                    if (!double.TryParse(XMaxBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _viewXmax)) _viewXmax = 10.0;
                                    if (_viewXmax <= _viewXmin) { _viewXmin = -10.0; _viewXmax = 10.0; }
                                    double r = Math.Max(Math.Abs(_viewXmin), Math.Abs(_viewXmax)); if (r < 1e-9) r = 10.0; _viewXmin = -r; _viewXmax = r;
                                    _hasLive = true; _viewYmin = double.NaN; _viewYmax = double.NaN;
                                }
                            }
                            // Replot current view to show overlays + series
                            if (_hasLive) ReplotCurrentView();
                        }
                    }
                    catch {}
                }
            }
            catch(Exception ex)
            {
                ResultTextBox.Text = $"Error: {ex.Message}";
            }

        }

        private void Plot_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear();
            ResultTextBox.Clear();

            // Normal sampled plotting using one or more series over [Xmin, Xmax] with Step.
            if (!double.TryParse(XMinBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double xmin) ||
                !double.TryParse(XMaxBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double xmax) ||
                !double.TryParse(StepBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double step))
            {
                ErrorBox.Text = "Invalid range values. Use numbers for Xmin, Xmax, and Step.";
                return;
            }
            if (step <= 0 || xmax <= xmin)
            {
                ErrorBox.Text = "Ensure: Step > 0 and Xmax > Xmin.";
                return;
            }

            string input = InputTextBox.Text.Trim();
            input = PreprocessYAssignment(input);
            if (string.IsNullOrWhiteSpace(input))
            {
                ErrorBox.Text = "Enter definitions and expressions to plot.";
                return;
            }

            var lines = input.Split(new[] {'\n',';'}, StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim())
                             .Where(s => s.Length>0)
                             .ToArray();
            if (lines.Length == 0)
            {
                ErrorBox.Text = "Nothing to plot.";
                return;
            }
            
            var funcDefs = lines
                .Select(l => Regex.Match(l, @"^\s*([A-Za-z]\w*)\s*\(\s*([A-Za-z]\w*)\s*\)\s*=").Groups)
                .Where(g => g.Count > 2 && !string.IsNullOrEmpty(g[1].Value))
                .Select(g => (name: g[1].Value.Trim(), param: g[2].Value.Trim()))
                .Distinct()
                .ToList();

            var verticalLines = lines
                .Select(l => Regex.Match(l, @"^x\s*=\s*([^;]+)$", RegexOptions.IgnoreCase))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value.Trim())
                .ToList();
            
            var newSeries = new List<Series>();
            int paletteIdx = _series.Count;

            if (funcDefs.Count > 0 || verticalLines.Count > 0)
            {
                foreach (var fd in funcDefs)
                {
                    newSeries.Add(new Series
                    {
                        Setup = lines,
                        Expr = $"{fd.name}(x)",
                        Mode = "linear",
                        Stroke = _palette[paletteIdx++ % _palette.Length]
                    });
                }
                foreach (var v in verticalLines)
                {
                    newSeries.Add(new Series
                    {
                        Setup = lines,
                        Expr = v,
                        Mode = "vertical",
                        Stroke = _palette[paletteIdx++ % _palette.Length]
                    });
                }
            }
            else
            {
                string expr = lines[lines.Length - 1];
                if (Regex.IsMatch(expr, @"^\s*[A-Za-z]\w*\s*\(\s*[A-Za-z]\w*\s*\)\s*="))
                {
                    ErrorBox.Text = "The last line is a function definition. Define functions and click Plot again, or put an expression in x on the last line.";
                    return;
                }
                var mVertical = Regex.Match(expr, @"^x\s*=\s*(.+)$", RegexOptions.IgnoreCase);
                if (mVertical.Success)
                {
                    newSeries.Add(new Series
                    {
                        Setup = lines.Take(lines.Length - 1).ToArray(),
                        Expr = mVertical.Groups[1].Value.Trim(),
                        Mode = "vertical",
                        Stroke = _palette[paletteIdx % _palette.Length]
                    });
                }
                else
                {
                    newSeries.Add(new Series
                    {
                        Setup = lines.Take(lines.Length - 1).ToArray(),
                        Expr = expr,
                        Mode = "linear",
                        Stroke = _palette[paletteIdx % _palette.Length]
                    });
                }
            }

            // Append new series to the plot collection with de-duplication by Expr label
            foreach (var ns in newSeries)
            {
                var existing = _series.FirstOrDefault(s => string.Equals(s.Expr.Trim(), ns.Expr.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Setup = ns.Setup;
                    existing.Mode = ns.Mode;
                }
                else
                {
                    _series.Add(ns);
                }
            }
            
            double r = Math.Max(Math.Abs(xmin), Math.Abs(xmax));
            if (r < 1e-9) r = 10.0;
            _viewXmin = -r;
            _viewXmax = r;
            _hasLive = true;
            _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
            _viewYmin = double.NaN;
            _viewYmax = double.NaN;
            ReplotCurrentView();
        }

        private void HelpMenu_Click(object sender, RoutedEventArgs e)
        {
            string helpText =
                    "Valid tokens and syntax:\n" +
                    "  Integers (e.g., 10)\n" +
                    "  Floats (e.g., 23.45, 1e3, 2.5E-4)\n" +
                    "  Identifiers (variables/functions): start with a letter, then letters/digits/_\n" +
                    "  Operators: +  -  *  /  %  ^  (power is LEFT-associative)\n" +
                    "  Parentheses: ( )\n\n" +
                    "Statements (separate by newline or ';'):\n" +
                    "  Assignment:    x = 10;   a = 2.5;\n" +
                    "  Function def:  f(x) = x^2 + 2*x + 1\n" +
                    "  For plotting/analysis: define y(x) = ... (used by plot/diff/integrate/root/tangent).\n" +
                    "  For transpiling: y(x) is optional; if omitted, the first defined function becomes the entry point.\n\n" +
                    "Built-ins (to enter in the expression box):\n" +
                    "  plot(x, dx, mode)        // buffers point (x, y(x)) with mode = linear|spline\n" +
                    "  diff(x0[, h])            // numeric derivative of y at x0\n" +
                    "  sdiff(x0)                // symbolic derivative via LUT where possible, else numeric\n" +
                    "  tangent(x0, halfW[, m])  // buffers a short tangent segment around x0\n" +
                    "  integrate(a, b[, n])     // trapezoid rule on y over [a,b]; also shades [a,b]\n" +
                    "  root_bisect(a,b[,tol][,maxIter])\n" +
                    "  root_newton(x0[,tol][,maxIter])\n" +
                    "  root_secant(x0,x1[,tol][,maxIter])\n" +
                    "  shade(a,b)               // mark [a,b] for area shading (signed)\n\n" +
                    "Vector/Matrix helpers (scalar results):\n" +
                    "  dot(a1,b1[,a2,b2,...])   // dot product of two equal-length vectors\n" +
                    "  norm(a1[,a2,...])        // Euclidean norm sqrt(sum(ai^2))\n" +
                    "  det(a,b,c,d)             // 2x2 determinant |a b; c d|\n" +
                    "  det(a11,..,a33)          // 3x3 determinant (row-major)\n\n" +
                    "Operator precedence (BODMAS/BIDMAS):\n" +
                    "  Highest: unary +/- ; then ^ (left-assoc) ; then * / % ; then + -\n\n" +
                    "Notes:\n" +
                    "  - Division by zero is detected.\n" +
                    "  - Using variables before assignment is an error.\n" +
                    "  - Fractional powers are supported (a^b) with real-domain checks (no negative base to fractional).\n" +
                    "  - Enter multiple lines: definitions first, then y(x), then analysis call (e.g., integrate(...)).\n";

            MessageBox.Show(helpText, "Help - Syntax and Tokens", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // -------- Transpiler/Compiler (GUI4) helpers --------
        private string? _lastBuildDir;
        private string? _lastExePath;

        private (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string args, string? workingDir = null)
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;
            using var p = new Process();
            p.StartInfo = psi;
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
        }

        private void ValidateSource_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ErrorBox.Clear();
                string src = SourceTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(src)) { ErrorBox.Text = "No source to validate."; return; }
                var lines = src.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim())
                               .Where(s => s.Length > 0)
                               .ToArray();
                ExprEvaluator.ResetState();
                foreach (var line in lines)
                {
                    string res = ExprEvaluator.EvaluateExpression(line);
                    if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
                    {
                        ErrorBox.Text = $"Interpreter validation error: {res}";
                        return;
                    }
                }
                ErrorBox.Text = "Interpreter source looks valid.";
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"Interpreter validation exception: {ex.Message}";
            }
        }

        private void Transpile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ErrorBox.Clear();
                string src = SourceTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(src)) { ErrorBox.Text = "No source to transpile."; return; }
                string csharp = ExprEvaluator.TranspileToCSharp(src);
                if (csharp.StartsWith("CSharp error:"))
                {
                    ErrorBox.Text = csharp;
                    TargetTextBox.Text = string.Empty;
                }
                else
                {
                    TargetTextBox.Text = csharp;
                }
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"Transpile exception: {ex.Message}";
            }
        }

        private bool CreateTempProject(string code, out string projDir, out string exePath)
        {
            projDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AP_Transpile_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(projDir);
            string proj = System.IO.Path.Combine(projDir, "GeneratedMath.csproj");
            string program = System.IO.Path.Combine(projDir, "Program.cs");
            // write files
            File.WriteAllText(program, code);
            File.WriteAllText(proj, "" +
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                "  <PropertyGroup>\n" +
                "    <OutputType>Exe</OutputType>\n" +
                "    <TargetFramework>net8.0</TargetFramework>\n" +
                "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                "    <Nullable>enable</Nullable>\n" +
                "    <AssemblyName>GeneratedMath</AssemblyName>\n" +
                "  </PropertyGroup>\n" +
                "</Project>\n");
            exePath = System.IO.Path.Combine(projDir, "bin", "Release", "net8.0", "GeneratedMath.exe");
            return true;
        }

        private void ValidateCSharp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ErrorBox.Clear();
                string code = TargetTextBox.Text;
                if (string.IsNullOrWhiteSpace(code)) { ErrorBox.Text = "No C# code to validate. Click Transpile first."; return; }
                if (!CreateTempProject(code, out var dir, out var exe)) { ErrorBox.Text = "Failed to create temp project."; return; }
                var (codeExit, so, se) = RunProcess("dotnet", "build -c Release", dir);
                if (codeExit == 0)
                {
                    ErrorBox.Text = "C# validation succeeded.";
                }
                else
                {
                    ErrorBox.Text = "C# validation failed:\n" + se + so;
                }
                try { Directory.Delete(dir, true); } catch { }
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"C# validation exception: {ex.Message}";
            }
        }

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ErrorBox.Clear();
                string code = TargetTextBox.Text;
                if (string.IsNullOrWhiteSpace(code)) { ErrorBox.Text = "No C# code to compile. Click Transpile first."; return; }
                if (!CreateTempProject(code, out var dir, out var exe)) { ErrorBox.Text = "Failed to create temp project."; return; }
                var (codeExit, so, se) = RunProcess("dotnet", "build -c Release", dir);
                if (codeExit == 0 && File.Exists(exe))
                {
                    _lastBuildDir = dir;
                    _lastExePath = exe;
                    ErrorBox.Text = "Build succeeded.";
                }
                else
                {
                    ErrorBox.Text = "Build failed:\n" + se + so;
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"Compile exception: {ex.Message}";
            }
        }

        private void RunExe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ErrorBox.Clear();
                if (string.IsNullOrEmpty(_lastExePath) || !File.Exists(_lastExePath))
                {
                    ErrorBox.Text = "No built executable. Click Compile first.";
                    return;
                }
                string arg = RunArgBox.Text?.Trim() ?? "0";
                var (codeExit, so, se) = RunProcess(_lastExePath, arg, _lastBuildDir);
                ResultTextBox.Text = so.Trim();
                if (codeExit != 0)
                {
                    ErrorBox.Text = $"Program exited with code {codeExit}.\n" + se;
                }
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"Run exception: {ex.Message}";
            }
        }

        // Canvas helpers
        private void ClearCanvas()
        {
            if (PlotCanvas == null) return;
            PlotCanvas.Children.Clear();
            if (PlotCanvas.RenderTransform == null)
            {
                PlotCanvas.RenderTransform = _plotTransform;
            }
            PlotCanvas.UpdateLayout();
        }

        // Re-sample and draw current live view
        private void ReplotCurrentView()
        {
            if (PlotCanvas == null || !_hasLive) return;
            
            double r = Math.Max(Math.Abs(_viewXmin), Math.Abs(_viewXmax));
            if (r < 1e-9) r = 10.0;
            _viewXmin = -r;
            _viewXmax = r;

            double width = Math.Max(PlotCanvas.ActualWidth, 10);
            int targetSamples = (int)Math.Clamp(Math.Round(width / 2.5), 200, 2000);
            double xrange = Math.Max(1e-9, _viewXmax - _viewXmin);
            double step = xrange / Math.Max(1, (targetSamples - 1));

            // Sample each series independently with its setup
            var seriesData = new List<(List<Point> Data, Brush Stroke, string Label, string Mode)>();
            foreach (var s in _series)
            {
                try
                {
                    ExprEvaluator.ResetState();
                    foreach (var stmt in s.Setup)
                    {
                        string res = ExprEvaluator.EvaluateExpression(stmt);
                        if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
                        {
                            ErrorBox.Text = $"Setup error: {res}";
                            seriesData.Clear();
                            break;
                        }
                    }
                    if (seriesData.Count == 0 && ErrorBox.Text.StartsWith("Setup error:")) break;

                    var pts = new List<Point>(targetSamples);
                    if (s.Mode == "vertical")
                    {
                        string xValStr = ExprEvaluator.EvaluateExpression(s.Expr);
                        if (double.TryParse(xValStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double xv))
                        {
                            // Placeholders for vertical line
                            pts.Add(new Point(xv, 0));
                        }
                    }
                    else
                    {
                        double x = _viewXmin;
                        for (int i = 0; i < targetSamples; i++)
                        {
                            string yStr = ExprEvaluator.EvaluateExprForX(s.Expr, x);
                            if (double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                                && !double.IsNaN(y) && !double.IsInfinity(y))
                            {
                                pts.Add(new Point(x, y));
                            }
                            x += step;
                        }
                    }

                    if (pts.Count >= 1)
                    {
                        string label = s.Expr;
                        var m = Regex.Match(s.Expr, @"^\s*([A-Za-z]\w*)\s*\(\s*x\s*\)\s*$");
                        if (m.Success) label = m.Groups[1].Value + "(x)";
                        else if (s.Mode == "vertical") label = "x=" + s.Expr;
                        seriesData.Add((pts, s.Stroke, label, s.Mode));
                    }
                }
                catch
                {
                    
                }
            }

            if (seriesData.Count > 0)
            {
                DrawPlotMulti(seriesData, _viewXmin, _viewXmax);
            }
        }

        private void LivePlotCheck_Changed(object sender, RoutedEventArgs e)
        {
            _livePlotEnabled = LivePlotCheck?.IsChecked == true;
            if (!PlotCanvas?.IsLoaded ?? true) return;

            if (_livePlotEnabled)
            {
                if (!_hasLive)
                {
                    string input = InputTextBox.Text.Trim();
                    var lines = input.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim())
                                     .Where(s => s.Length > 0)
                                     .ToArray();
                    if (lines.Length >= 1)
                    {
                        string expr = lines[lines.Length - 1];
                        var setup = lines.Take(lines.Length - 1).ToArray();
                        try
                        {
                            ExprEvaluator.ResetState();
                            foreach (var stmt in setup)
                            {
                                string res = ExprEvaluator.EvaluateExpression(stmt);
                                if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
                                {
                                    ErrorBox.Text = $"Setup error: {res}";
                                    return;
                                }
                            }
                            _liveExpr = expr;
                            _liveSetup = setup;
                            _hasLive = true;
                            if (!double.TryParse(XMinBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _viewXmin)) _viewXmin = -10.0;
                            if (!double.TryParse(XMaxBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _viewXmax)) _viewXmax = 10.0;
                            if (_viewXmax <= _viewXmin) { _viewXmin = -10.0; _viewXmax = 10.0; }
                        }
                        catch {}
                    }
                }
                if (_hasLive)
                {
                    _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                    _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
                    _viewYmin = double.NaN;
                    _viewYmax = double.NaN;
                    ReplotCurrentView();
                }
            }
        }

        private void PlotCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_hasLive)
            {
                ReplotCurrentView();
            }
        }

        // Mouse interaction handlers for panning and zooming on PlotCanvas
        private void PlotCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PlotCanvas == null) return;

            // Live mode: zoom symmetrically around 0 so origin stays centered, for BOTH axes
            if (_livePlotEnabled && _hasLive)
            {
                double zoom = e.Delta > 0 ? 1.1 : 1.0 / 1.1; // wheel up -> zoom in

                // X axis
                double xrange = Math.Max(1e-9, _viewXmax - _viewXmin);
                double newXRange = xrange / zoom;
                double rx = Math.Max(newXRange / 2.0, 1e-6);
                _viewXmin = -rx;
                _viewXmax = rx;

                // Y axis
                if (!double.IsNaN(_viewYmin) && !double.IsNaN(_viewYmax) && _viewYmax > _viewYmin)
                {
                    double yrange = Math.Max(1e-9, _viewYmax - _viewYmin);
                    double newYRange = yrange / zoom;
                    double ry = Math.Max(newYRange / 2.0, 1e-6);
                    _viewYmin = -ry;
                    _viewYmax = ry;
                }

                // Reset transform to avoid double transforms
                _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _translateTransform.X = 0.0; _translateTransform.Y = 0.0;

                ReplotCurrentView();
                e.Handled = true;
                return;
            }

            if (_hasLive)
            {
                double xrange = Math.Max(1e-9, _viewXmax - _viewXmin);
                double zoom = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
                double newRange = xrange / zoom;
                double r = Math.Max(newRange / 2.0, 1e-6);
                _viewXmin = -r;
                _viewXmax = r;
                _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
                ReplotCurrentView();
                e.Handled = true;
                return;
            }

            _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
            e.Handled = true;
        }

        private void PlotCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (PlotCanvas == null) return;
            _isPanning = true;
            _lastPanPoint = e.GetPosition(PlotCanvas);
            PlotCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void PlotCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (PlotCanvas == null) return;
            _isPanning = false;
            PlotCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void PlotCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning || PlotCanvas == null) return;
            Point p = e.GetPosition(PlotCanvas);
            Vector delta = p - _lastPanPoint;
            _lastPanPoint = p;

            if (_livePlotEnabled && _hasLive)
            {
                _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
                ReplotCurrentView();
            }
            else
            {
                _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
                if (_hasLive) ReplotCurrentView();
            }
            e.Handled = true;
        }

        private void PlotCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_livePlotEnabled && _hasLive)
            {
                // Reset world range to inputs or defaults
                if (!double.TryParse(XMinBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double xmin)) xmin = -10.0;
                if (!double.TryParse(XMaxBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double xmax)) xmax = 10.0;
                if (xmax <= xmin) { xmin = -10.0; xmax = 10.0; }
                double r = Math.Max(Math.Abs(xmin), Math.Abs(xmax));
                if (r < 1e-9) r = 10.0;
                _viewXmin = -r;
                _viewXmax = r;
                _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
                ReplotCurrentView();
            }
            else
            {
                // Reset transform view
                _scaleTransform.ScaleX = 1.0;
                _scaleTransform.ScaleY = 1.0;
                _translateTransform.X = 0.0;
                _translateTransform.Y = 0.0;
            }
            e.Handled = true;
        }
        
        private void DrawPlot(List<Point> data, double xmin, double xmax)
        {
            DrawPlot(data, xmin, xmax, "linear");
        }

        // New: Multi-series renderer
        private void DrawPlotMulti(List<(List<Point> Data, Brush Stroke, string Label, string Mode)> seriesData, double xmin, double xmax)
        {
            if (PlotCanvas == null) return;
            PlotCanvas.Children.Clear();
            PlotCanvas.UpdateLayout();

            double width = Math.Max(PlotCanvas.ActualWidth, 10);
            double height = Math.Max(PlotCanvas.ActualHeight, 10);
            if (width < 50) width = 800;
            if (height < 50) height = 500;
            double pad = PlotPad;

            // Symmetric X range around 0 so (0,0) is at canvas center
            double xAbsMax = Math.Max(Math.Abs(xmin), Math.Abs(xmax));
            if (xAbsMax < 1e-9) xAbsMax = 10.0;
            double sxmin = -xAbsMax;
            double sxmax = xAbsMax;

            // Determine combined Y range across all series, then symmetrize about 0
            double ymin, ymax;
            if (_livePlotEnabled && _hasLive && !double.IsNaN(_viewYmin) && !double.IsNaN(_viewYmax) && _viewYmax > _viewYmin)
            {
                ymin = _viewYmin; ymax = _viewYmax;
            }
            else
            {
                double yMinData = double.PositiveInfinity;
                double yMaxData = double.NegativeInfinity;
                foreach (var item in seriesData)
                {
                    if (item.Mode == "vertical") continue;
                    var Data = item.Data;
                    if (Data.Count == 0) continue;
                    double dmin = Data.Min(p => p.Y);
                    double dmax = Data.Max(p => p.Y);
                    if (dmin < yMinData) yMinData = dmin;
                    if (dmax > yMaxData) yMaxData = dmax;
                }
                if (!double.IsFinite(yMinData) || !double.IsFinite(yMaxData)) { yMinData = -1; yMaxData = 1; }
                if (Math.Abs(yMaxData - yMinData) < 1e-12)
                {
                    double padY0 = Math.Max(1.0, Math.Abs(yMaxData));
                    ymin = yMinData - 0.5 * padY0;
                    ymax = yMaxData + 0.5 * padY0;
                }
                else
                {
                    double range = yMaxData - yMinData;
                    double padY = range * 0.1;
                    ymin = yMinData - padY;
                    ymax = yMaxData + padY;
                }
            }
            double rY = Math.Max(Math.Abs(ymin), Math.Abs(ymax));
            if (rY < 1e-9) rY = 1.0;
            ymin = -rY; ymax = rY;
            if (_livePlotEnabled && _hasLive)
            {
                _viewYmin = ymin; _viewYmax = ymax;
            }

            Func<double, double> xToPx = xv => pad + (xv - sxmin) / (sxmax - sxmin) * (width - 2 * pad);
            Func<double, double> yToPy = yv => height - pad - (yv - ymin) / (ymax - ymin) * (height - 2 * pad);

            double NiceStep(double range, int targetTicks = 8)
            {
                if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return 1.0;
                double raw = range / Math.Max(1, targetTicks);
                double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-12))));
                double m = raw / p;
                if (m < 1.5) return 1 * p;
                if (m < 3) return 2 * p;
                if (m < 7) return 5 * p;
                return 10 * p;
            }
            string NumLabel(double v)
            {
                if (Math.Abs(v - Math.Round(v)) < 1e-9) return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
                return v.ToString("0.###", CultureInfo.InvariantCulture);
            }

            // Grid (vertical and horizontal)
            {
                double xrange = sxmax - sxmin;
                double xstep = NiceStep(xrange);
                double xstart = Math.Ceiling(sxmin / xstep) * xstep;
                int guard = 0;
                for (double xv = xstart; xv <= sxmax + 1e-12 && guard < 20000; xv += xstep, guard++)
                {
                    double px = xToPx(xv);
                    var grid = new Line { X1 = px, X2 = px, Y1 = pad, Y2 = height - pad, Stroke = Brushes.Gainsboro, StrokeThickness = 1 };
                    PlotCanvas.Children.Add(grid);
                }
                double yrange = ymax - ymin;
                double ystep = NiceStep(yrange);
                double ystart = Math.Ceiling(ymin / ystep) * ystep;
                guard = 0;
                for (double yv = ystart; yv <= ymax + 1e-12 && guard < 20000; yv += ystep, guard++)
                {
                    double py = yToPy(yv);
                    var grid = new Line { X1 = pad, X2 = width - pad, Y1 = py, Y2 = py, Stroke = Brushes.Gainsboro, StrokeThickness = 1 };
                    PlotCanvas.Children.Add(grid);
                }
            }

            // Axes
            {
                var x0 = xToPx(0);
                PlotCanvas.Children.Add(new Line { X1 = x0, X2 = x0, Y1 = pad, Y2 = height - pad, Stroke = Brushes.Black, StrokeThickness = 1.5 });
                var y0 = yToPy(0);
                PlotCanvas.Children.Add(new Line { X1 = pad, X2 = width - pad, Y1 = y0, Y2 = y0, Stroke = Brushes.Black, StrokeThickness = 1.5 });
            }

            // Ticks
            {
                double xrange = sxmax - sxmin;
                double step = NiceStep(xrange);
                double start = Math.Ceiling(sxmin / step) * step;
                double yBase = height - pad; double tickLen = 8; int guard = 0;
                for (double xv = start; xv <= sxmax + 1e-12 && guard < 10000; xv += step, guard++)
                {
                    double px = xToPx(xv);
                    PlotCanvas.Children.Add(new Line { X1 = px, X2 = px, Y1 = yBase, Y2 = yBase - tickLen, Stroke = Brushes.Gray, StrokeThickness = 1 });
                    var tb = new TextBlock { Text = NumLabel(xv), FontSize = 12, Foreground = Brushes.Black };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    PlotCanvas.Children.Add(tb);
                    Canvas.SetLeft(tb, px - tb.DesiredSize.Width / 2);
                    Canvas.SetTop(tb, yBase + 4);
                }
                double yrange = ymax - ymin; step = NiceStep(yrange); start = Math.Ceiling(ymin / step) * step; double xBase = pad; guard = 0;
                for (double yv = start; yv <= ymax + 1e-12 && guard < 10000; yv += step, guard++)
                {
                    double py = yToPy(yv);
                    PlotCanvas.Children.Add(new Line { X1 = xBase, X2 = xBase + tickLen, Y1 = py, Y2 = py, Stroke = Brushes.Gray, StrokeThickness = 1 });
                    var tb = new TextBlock { Text = NumLabel(yv), FontSize = 12, Foreground = Brushes.Black };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    PlotCanvas.Children.Add(tb);
                    Canvas.SetLeft(tb, xBase - tb.DesiredSize.Width - 6);
                    Canvas.SetTop(tb, py - tb.DesiredSize.Height / 2);
                }
            }

            // Labels
            var xLabel = new TextBlock { Text = "X", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
            PlotCanvas.Children.Add(xLabel);
            Canvas.SetLeft(xLabel, xToPx(sxmax) + 10);
            Canvas.SetTop(xLabel, yToPy(0) + 8);
            var yLabel = new TextBlock { Text = "Y", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
            PlotCanvas.Children.Add(yLabel);
            Canvas.SetLeft(yLabel, xToPx(0) - 14);
            Canvas.SetTop(yLabel, yToPy(ymax) - 20);
            
            try
            {
                if (seriesData.Count > 0 && ExprEvaluator.HasShadingRange())
                {
                    var last = seriesData[seriesData.Count - 1].Data.OrderBy(p => p.X).ToList();
                    var pr = ExprEvaluator.GetPlotRange();
                    double a = pr.Item1; double b = pr.Item2; if (b < a) { var t = a; a = b; b = t; }
                    if (last.Count >= 2 && !(b < last[0].X || a > last[last.Count - 1].X))
                    {
                        List<Point> clip = new List<Point>();
                        void AddAt(double x0)
                        {
                            for (int i = 0; i < last.Count - 1; i++)
                            {
                                var p1 = last[i]; var p2 = last[i + 1];
                                if ((x0 >= p1.X && x0 <= p2.X) || (x0 >= p2.X && x0 <= p1.X))
                                {
                                    if (Math.Abs(p2.X - p1.X) < 1e-12) return;
                                    double tt = (x0 - p1.X) / (p2.X - p1.X);
                                    double y0 = p1.Y + tt * (p2.Y - p1.Y);
                                    clip.Add(new Point(x0, y0));
                                    return;
                                }
                            }
                        }
                        foreach (var p in last) if (p.X >= a && p.X <= b) clip.Add(p);
                        if (clip.Count == 0 || clip[0].X > a + 1e-12) AddAt(a);
                        if (clip.Count == 0 || clip[clip.Count - 1].X < b - 1e-12) AddAt(b);
                        clip = clip.OrderBy(p => p.X).ToList();
                        List<Point> seg = new List<Point>();
                        int sgn(double yy) => yy >= 0 ? 1 : -1;
                        void Flush()
                        {
                            if (seg.Count < 2) return;
                            int sign = sgn(seg[0].Y);
                            var poly = new Polygon
                            {
                                Fill = sign > 0 ? new SolidColorBrush(Color.FromArgb(90, 50, 205, 50)) : new SolidColorBrush(Color.FromArgb(90, 220, 20, 60)),
                                StrokeThickness = 0
                            };
                            var pts = new PointCollection();
                            pts.Add(new System.Windows.Point(xToPx(seg[0].X), yToPy(0)));
                            foreach (var p in seg) pts.Add(new System.Windows.Point(xToPx(p.X), yToPy(p.Y)));
                            pts.Add(new System.Windows.Point(xToPx(seg[seg.Count - 1].X), yToPy(0)));
                            poly.Points = pts;
                            PlotCanvas.Children.Add(poly);
                        }
                        for (int i = 0; i < clip.Count; i++)
                        {
                            var p = clip[i];
                            if (seg.Count == 0) seg.Add(Math.Abs(p.Y) < 1e-15 ? new Point(p.X, 0) : p);
                            else
                            {
                                var prev = seg[seg.Count - 1];
                                if (Math.Sign(prev.Y) == Math.Sign(p.Y) || Math.Abs(p.Y) < 1e-15) seg.Add(p.Y == 0 ? new Point(p.X, 0) : p);
                                else
                                {
                                    double x1 = prev.X, y1 = prev.Y, x2 = p.X, y2 = p.Y;
                                    if (Math.Abs(x2 - x1) > 1e-12)
                                    {
                                        double t0 = -y1 / (y2 - y1);
                                        double xc = x1 + t0 * (x2 - x1);
                                        var cross = new Point(xc, 0);
                                        seg.Add(cross); Flush(); seg.Clear(); seg.Add(cross); seg.Add(p);
                                    }
                                    else { Flush(); seg.Clear(); seg.Add(p); }
                                }
                            }
                        }
                        Flush();
                    }
                }
            }
            catch { }
            
            foreach (var item in seriesData)
            {
                var Data = item.Data;
                var Stroke = item.Stroke;

                if (item.Mode == "vertical" && Data.Count > 0)
                {
                    double xv = Data[0].X;
                    double px = xToPx(xv);
                    PlotCanvas.Children.Add(new Line { X1 = px, X2 = px, Y1 = pad, Y2 = height - pad, Stroke = Stroke, StrokeThickness = 2.5 });
                    continue;
                }

                var sortedData = Data.OrderBy(p => p.X).ToList();
                double yRangeForJump = Math.Max(1e-12, ymax - ymin);
                double jumpThresh = yRangeForJump * 0.4;
                void AddSegment(List<Point> seg)
                {
                    if (seg.Count < 2) return;
                    var poly = new Polyline { Stroke = Stroke, StrokeThickness = 2.5 };
                    foreach (var p in seg) poly.Points.Add(new System.Windows.Point(xToPx(p.X), yToPy(p.Y)));
                    PlotCanvas.Children.Add(poly);
                }
                List<Point> segBuf = new List<Point>();
                for (int i = 0; i < sortedData.Count; i++)
                {
                    var p = sortedData[i];
                    if (segBuf.Count == 0) { segBuf.Add(p); continue; }
                    var prev = segBuf[segBuf.Count - 1];
                    bool bigJump = Math.Abs(p.Y - prev.Y) > jumpThresh || Math.Abs(p.Y) > 1e6 || Math.Abs(prev.Y) > 1e6;
                    if (bigJump) { AddSegment(segBuf); segBuf.Clear(); segBuf.Add(p); }
                    else segBuf.Add(p);
                }
                AddSegment(segBuf);
            }
            
            if (_overlays.Count > 0)
            {
                foreach (var seg in _overlays)
                {
                    var line = new Line
                    {
                        X1 = xToPx(seg.A.X), Y1 = yToPy(seg.A.Y),
                        X2 = xToPx(seg.B.X), Y2 = yToPy(seg.B.Y),
                        Stroke = seg.Stroke, StrokeThickness = 3.0
                    };
                    PlotCanvas.Children.Add(line);
                }
            }
            
            if (seriesData.Count > 0)
            {
                double legendPad = 8;
                var legendPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Background = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                    Margin = new Thickness(0)
                };
                legendPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromArgb(50, 0, 0, 0), BlurRadius = 6, ShadowDepth = 1
                };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in seriesData)
                {
                    if (!seen.Add(item.Label.Trim())) continue;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 0) };
                    var swatch = new Rectangle { Width = 14, Height = 14, Fill = item.Stroke, Stroke = Brushes.Black, StrokeThickness = 0.5, Margin = new Thickness(0, 0, 6, 0) };
                    var labelTb = new TextBlock { Text = item.Label, FontSize = 12, Foreground = Brushes.Black };
                    row.Children.Add(swatch);
                    row.Children.Add(labelTb);
                    legendPanel.Children.Add(row);
                }
                PlotCanvas.Children.Add(legendPanel);
                legendPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(legendPanel, width - pad - legendPanel.DesiredSize.Width - legendPad);
                Canvas.SetTop(legendPanel, pad + legendPad);
            }
        }

        private void DrawPlot(List<Point> data, double xmin, double xmax, string mode)
        {
            if (PlotCanvas == null) return;
            PlotCanvas.Children.Clear();
            PlotCanvas.UpdateLayout();

            double width = Math.Max(PlotCanvas.ActualWidth, 10);
            double height = Math.Max(PlotCanvas.ActualHeight, 10);
            if (width < 50) width = 800;
            if (height < 50) height = 500;
            double pad = PlotPad;
            
            double xAbsMax = Math.Max(Math.Abs(xmin), Math.Abs(xmax));
            if (xAbsMax < 1e-9) xAbsMax = 10.0;
            double sxmin = -xAbsMax;
            double sxmax = xAbsMax;
            
            double ymin, ymax;
            if (_livePlotEnabled && _hasLive && !double.IsNaN(_viewYmin) && !double.IsNaN(_viewYmax) && _viewYmax > _viewYmin)
            {
                ymin = _viewYmin;
                ymax = _viewYmax;
            }
            else
            {
                double yMinData = data.Min(p => p.Y);
                double yMaxData = data.Max(p => p.Y);
                if (Math.Abs(yMaxData - yMinData) < 1e-12)
                {
                    double padY = Math.Max(1.0, Math.Abs(yMaxData));
                    ymin = yMinData - 0.5 * padY;
                    ymax = yMaxData + 0.5 * padY;
                }
                else
                {
                    double range = yMaxData - yMinData;
                    double padY = range * 0.1;
                    ymin = yMinData - padY;
                    ymax = yMaxData + padY;
                }
            }
            
            double rY = Math.Max(Math.Abs(ymin), Math.Abs(ymax));
            if (rY < 1e-9) rY = 1.0;
            ymin = -rY; ymax = rY;
            if (_livePlotEnabled && _hasLive)
            {
                _viewYmin = ymin;
                _viewYmax = ymax;
            }

            Func<double, double> xToPx = xv => pad + (xv - sxmin) / (sxmax - sxmin) * (width - 2 * pad);
            Func<double, double> yToPy = yv => height - pad - (yv - ymin) / (ymax - ymin) * (height - 2 * pad);
            
            double NiceStep(double range, int targetTicks = 8)
            {
                if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return 1.0;
                double raw = range / Math.Max(1, targetTicks);
                double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-12))));
                double m = raw / p;
                if (m < 1.5) return 1 * p;
                if (m < 3) return 2 * p;
                if (m < 7) return 5 * p;
                return 10 * p;
            }

            string NumLabel(double v)
            {
                if (Math.Abs(v - Math.Round(v)) < 1e-9) return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
                return v.ToString("0.###", CultureInfo.InvariantCulture);
            }

            // Grid lines (vertical)
            {
                double xrange = sxmax - sxmin;
                double step = NiceStep(xrange);
                double start = Math.Ceiling(sxmin / step) * step;
                int guard = 0;
                for (double xv = start; xv <= sxmax + 1e-12 && guard < 20000; xv += step, guard++)
                {
                    double px = xToPx(xv);
                    var grid = new Line { X1 = px, X2 = px, Y1 = pad, Y2 = height - pad, Stroke = Brushes.Gainsboro, StrokeThickness = 1 };
                    PlotCanvas.Children.Add(grid);
                }
            }

            // Grid lines (horizontal)
            {
                double yrange = ymax - ymin;
                double step = NiceStep(yrange);
                double start = Math.Ceiling(ymin / step) * step;
                int guard = 0;
                for (double yv = start; yv <= ymax + 1e-12 && guard < 20000; yv += step, guard++)
                {
                    double py = yToPy(yv);
                    var grid = new Line { X1 = pad, X2 = width - pad, Y1 = py, Y2 = py, Stroke = Brushes.Gainsboro, StrokeThickness = 1 };
                    PlotCanvas.Children.Add(grid);
                }
            }

            // Axes through center (0,0)
            {
                var x0 = xToPx(0);
                var vline = new Line { X1 = x0, X2 = x0, Y1 = pad, Y2 = height - pad, Stroke = Brushes.Black, StrokeThickness = 1.5 };
                PlotCanvas.Children.Add(vline);
                var y0 = yToPy(0);
                var hline = new Line { X1 = pad, X2 = width - pad, Y1 = y0, Y2 = y0, Stroke = Brushes.Black, StrokeThickness = 1.5 };
                PlotCanvas.Children.Add(hline);
            }

            // X ticks
            {
                double xrange = sxmax - sxmin;
                double step = NiceStep(xrange);
                double start = Math.Ceiling(sxmin / step) * step;
                double yBase = height - pad;
                double tickLen = 8;
                int guard = 0;
                for (double xv = start; xv <= sxmax + 1e-12 && guard < 10000; xv += step, guard++)
                {
                    double px = xToPx(xv);
                    var t = new Line { X1 = px, X2 = px, Y1 = yBase, Y2 = yBase - tickLen, Stroke = Brushes.Gray, StrokeThickness = 1 };
                    PlotCanvas.Children.Add(t);

                    var tb = new TextBlock { Text = NumLabel(xv), FontSize = 12, Foreground = Brushes.Black };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    PlotCanvas.Children.Add(tb);
                    Canvas.SetLeft(tb, px - tb.DesiredSize.Width / 2);
                    Canvas.SetTop(tb, yBase + 4);
                }
            }

            // Y ticks
            {
                double yrange = ymax - ymin;
                double step = NiceStep(yrange);
                double start = Math.Ceiling(ymin / step) * step;
                double xBase = pad;
                double tickLen = 8;
                int guard = 0;
                for (double yv = start; yv <= ymax + 1e-12 && guard < 10000; yv += step, guard++)
                {
                    double py = yToPy(yv);
                    var t = new Line { X1 = xBase, X2 = xBase + tickLen, Y1 = py, Y2 = py, Stroke = Brushes.Gray, StrokeThickness = 1 };
                    PlotCanvas.Children.Add(t);

                    var tb = new TextBlock { Text = NumLabel(yv), FontSize = 12, Foreground = Brushes.Black };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    PlotCanvas.Children.Add(tb);
                    Canvas.SetLeft(tb, xBase - tb.DesiredSize.Width - 6);
                    Canvas.SetTop(tb, py - tb.DesiredSize.Height / 2);
                }
            }

            // Axes labels
            var xLabel = new TextBlock { Text = "X", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
            PlotCanvas.Children.Add(xLabel);
            Canvas.SetLeft(xLabel, xToPx(sxmax) + 10);
            Canvas.SetTop(xLabel, yToPy(0) + 8);

            var yLabel = new TextBlock { Text = "Y", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
            PlotCanvas.Children.Add(yLabel);
            Canvas.SetLeft(yLabel, xToPx(0) - 14);
            Canvas.SetTop(yLabel, yToPy(ymax) - 20);

            try
            {
                if (ExprEvaluator.HasShadingRange())
                {
                    var pr = ExprEvaluator.GetPlotRange();
                    double a = pr.Item1;
                    double b = pr.Item2;
                    if (b < a)
                    {
                        var tmp = a;
                        a = b;
                        b = tmp;
                    }

                    var sorted = data.OrderBy(p => p.X).ToList();
                    if (sorted.Count >= 2 && !(b < sorted[0].X || a > sorted[sorted.Count - 1].X))
                    {
                        List<Point> clip = new List<Point>();

                        void AddAt(double x0)
                        {
                            for (int i = 0; i < sorted.Count - 1; i++)
                            {
                                var p1 = sorted[i];
                                var p2 = sorted[i + 1];
                                if ((x0 >= p1.X && x0 <= p2.X) || (x0 >= p2.X && x0 <= p1.X))
                                {
                                    if (Math.Abs(p2.X - p1.X) < 1e-12) return;
                                    double t = (x0 - p1.X) / (p2.X - p1.X);
                                    double y0 = p1.Y + t * (p2.Y - p1.Y);
                                    clip.Add(new Point(x0, y0));
                                    return;
                                }
                            }
                        }

                        foreach (var p in sorted)
                        {
                            if (p.X >= a && p.X <= b) clip.Add(p);
                        }

                        if (clip.Count == 0 || clip[0].X > a + 1e-12) AddAt(a);
                        if (clip.Count == 0 || clip[clip.Count - 1].X < b - 1e-12) AddAt(b);
                        clip = clip.OrderBy(p => p.X).ToList();

                        List<Point> seg = new List<Point>();
                        int sgn(double y) => y >= 0 ? 1 : -1;

                        void Flush()
                        {
                            if (seg.Count < 2) return;
                            int sign = sgn(seg[0].Y);
                            var poly = new Polygon
                            {
                                Fill = sign > 0
                                    ? new SolidColorBrush(Color.FromArgb(90, 50, 205, 50))
                                    : new SolidColorBrush(Color.FromArgb(90, 220, 20, 60)),
                                StrokeThickness = 0
                            };
                            var pts = new PointCollection();
                            pts.Add(new System.Windows.Point(xToPx(seg[0].X), yToPy(0)));
                            foreach (var p in seg)
                                pts.Add(new System.Windows.Point(xToPx(p.X), yToPy(p.Y)));
                            pts.Add(new System.Windows.Point(xToPx(seg[seg.Count - 1].X), yToPy(0)));
                            poly.Points = pts;
                            PlotCanvas.Children.Add(poly);
                        }

                        for (int i = 0; i < clip.Count; i++)
                        {
                            var p = clip[i];
                            if (seg.Count == 0)
                            {
                                if (Math.Abs(p.Y) < 1e-15)
                                    seg.Add(new Point(p.X, 0));
                                else
                                    seg.Add(p);
                            }
                            else
                            {
                                var prev = seg[seg.Count - 1];
                                if (Math.Sign(prev.Y) == Math.Sign(p.Y) || Math.Abs(p.Y) < 1e-15)
                                {
                                    seg.Add(p.Y == 0 ? new Point(p.X, 0) : p);
                                }
                                else
                                {
                                    double x1 = prev.X, y1 = prev.Y, x2 = p.X, y2 = p.Y;
                                    if (Math.Abs(x2 - x1) > 1e-12)
                                    {
                                        double t0 = -y1 / (y2 - y1);
                                        double xc = x1 + t0 * (x2 - x1);
                                        var cross = new Point(xc, 0);
                                        seg.Add(cross);
                                        Flush();
                                        seg.Clear();
                                        seg.Add(cross);
                                        seg.Add(p);
                                    }
                                    else
                                    {
                                        Flush();
                                        seg.Clear();
                                        seg.Add(p);
                                    }
                                }
                            }
                        }

                        Flush();
                    }
                }
            }
            catch {}
            
            var sortedData = data.OrderBy(p => p.X).ToList();
            double yRangeForJump = Math.Max(1e-12, ymax - ymin);
            double jumpThresh = yRangeForJump * 0.4;
            bool wantSpline = string.Equals(mode, "spline", StringComparison.OrdinalIgnoreCase) && sortedData.Count >= 4;
            
            void AddSegment(List<Point> seg)
            {
                if (seg.Count < 2) return;
                var poly = new Polyline { Stroke = Brushes.DarkBlue, StrokeThickness = 2.5 };
                foreach (var p in seg)
                    poly.Points.Add(new System.Windows.Point(xToPx(p.X), yToPy(p.Y)));
                PlotCanvas.Children.Add(poly);
            }
            
            if (wantSpline)
            {
                for (int i = 1; i < sortedData.Count; i++)
                {
                    if (Math.Abs(sortedData[i].Y - sortedData[i-1].Y) > jumpThresh)
                    {
                        wantSpline = false; break;
                    }
                }
            }

            if (wantSpline)
            {
                var pts = sortedData.ToList();
                pts.Insert(0, pts[0]);
                pts.Add(pts[pts.Count - 1]);
                var poly = new Polyline { Stroke = Brushes.DarkBlue, StrokeThickness = 2.5 };
                Point CR(Point p0, Point p1, Point p2, Point p3, double t)
                {
                    double t2 = t * t;
                    double t3 = t2 * t;
                    double x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                    double y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                    return new Point(x, y);
                }
                for (int i = 0; i < pts.Count - 3; i++)
                {
                    for (double t = 0; t <= 1.0; t += 0.05)
                    {
                        var c = CR(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], t);
                        poly.Points.Add(new System.Windows.Point(xToPx(c.X), yToPy(c.Y)));
                    }
                }
                PlotCanvas.Children.Add(poly);
            }
            else
            {
                List<Point> seg = new List<Point>();
                for (int i = 0; i < sortedData.Count; i++)
                {
                    var p = sortedData[i];
                    if (seg.Count == 0) { seg.Add(p); continue; }
                    var prev = seg[seg.Count - 1];
                    bool bigJump = Math.Abs(p.Y - prev.Y) > jumpThresh || Math.Abs(p.Y) > 1e6 || Math.Abs(prev.Y) > 1e6;
                    if (bigJump)
                    {
                        AddSegment(seg);
                        seg.Clear();
                        seg.Add(p);
                    }
                    else
                    {
                        seg.Add(p);
                    }
                }
                AddSegment(seg);
            }

        }

        private void ResetPlots_Click(object sender, RoutedEventArgs e)
        {
            _series.Clear();
            _overlays.Clear();
            _hasLive = false;
            _viewXmin = -10; _viewXmax = 10; _viewYmin = double.NaN; _viewYmax = double.NaN;
            _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
            ClearCanvas();
        }

    }
}
