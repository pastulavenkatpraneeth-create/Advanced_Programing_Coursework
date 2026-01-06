using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

        // Pan/Zoom state
        private bool _isPanning = false;
        private Point _lastPanPoint;
        private ScaleTransform _scaleTransform = new ScaleTransform(1.0, 1.0);
        private TranslateTransform _translateTransform = new TranslateTransform(0.0, 0.0);
        private TransformGroup _plotTransform;
        private const double MinZoom = 0.1;
        private const double MaxZoom = 20.0;

        // Live plotting state
        private bool _livePlotEnabled = true;
        private bool _hasLive = false;
        private string _liveExpr = string.Empty;
        private string[] _liveSetup = Array.Empty<string>();
        private double _viewXmin = -10.0;
        private double _viewXmax = 10.0;

        private const double PlotPad = 50.0;
        private string _lastCompiledPath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            // Initialize transform group for interactive pan/zoom
            _plotTransform = new TransformGroup();
            _plotTransform.Children.Add(_scaleTransform); // scale first
            _plotTransform.Children.Add(_translateTransform); // then translate
            // PlotCanvas may be null during design-time
            this.Loaded += (s, e) =>
            {
                if (PlotCanvas != null)
                {
                    PlotCanvas.RenderTransform = _plotTransform;
                    PlotCanvas.SnapsToDevicePixels = true;
                }
            };
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
                    // Auto-plot interpreter-driven buffered data immediately (rollback to initial behavior)
                    try
                    {
                        if (ExprEvaluator.HasPlotData())
                        {
                            var data = ExprEvaluator.GetPlotData();
                            if (data != null && data.Length >= 2)
                            {
                                var pts = new List<Point>(data.Length);
                                foreach (var t in data) pts.Add(new Point(t.Item1, t.Item2));
                                var mode = ExprEvaluator.GetPlotMode() ?? "linear";
                                var r = ExprEvaluator.GetPlotRange();
                                ClearCanvas();
                                DrawPlot(pts, r.Item1, r.Item2, mode);
                                try { ExprEvaluator.ClearPlotData(); } catch {}
                            }
                            else
                            {
                                try { ExprEvaluator.ClearPlotData(); } catch {}
                            }
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

            // Normal sampled plotting using y(x) over [Xmin, Xmax] with Step.
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
            if (string.IsNullOrWhiteSpace(input))
            {
                ErrorBox.Text = "Enter definitions and a final expression in x to plot.";
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
            string expr = lines[lines.Length-1];
            var setup = lines.Take(lines.Length-1).ToArray();

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

            var points = new List<Point>();
            double x = xmin;
            int guard = 0;
            while (x <= xmax + 1e-12 && guard < 100000)
            {
                string yStr = ExprEvaluator.EvaluateExprForX(expr, x);
                if (yStr.StartsWith("Lexer") || yStr.StartsWith("Parser") || yStr.StartsWith("Runtime") || yStr.StartsWith("Error"))
                {
                    ErrorBox.Text = $"At x={x.ToString("0.###", CultureInfo.InvariantCulture)}: {yStr}";
                    return;
                }
                if (double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    && !double.IsNaN(y) && !double.IsInfinity(y))
                {
                    points.Add(new Point(x, y));
                }
                x += step;
                guard++;
            }
            if (points.Count < 2)
            {
                ErrorBox.Text = "Not enough points to plot.";
                return;
            }

            // Draw on embedded canvas in MainWindow
            DrawPlot(points, xmin, xmax);
            _liveExpr = expr;
            _liveSetup = setup;
            double r = Math.Max(Math.Abs(xmin), Math.Abs(xmax));
            if (r < 1e-9) r = 10.0;
            _viewXmin = -r;
            _viewXmax = r;
            _hasLive = true;
            _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
            if (_livePlotEnabled)
            {
                ReplotCurrentView();
            }
        }

        private bool PrepareYDefinition(out string[] setupLines)
        {
            setupLines = Array.Empty<string>();
            string input = InputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(input)) { ErrorBox.Text = "Enter definitions including y(x)."; return false; }
            var lines = input.Split(new[] {'\n',';'}, StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim())
                             .Where(s => s.Length>0)
                             .ToArray();
            if (lines.Length == 0) { ErrorBox.Text = "Nothing to evaluate."; return false; }
            setupLines = lines;
            ExprEvaluator.ResetState();
            foreach (var stmt in setupLines)
            {
                string res = ExprEvaluator.EvaluateExpression(stmt);
                if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
                {
                    ErrorBox.Text = $"Setup error: {res}";
                    return false;
                }
            }
            return true;
        }

        private void PlotDerivative_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear();
            ResultTextBox.Clear();
            if (!double.TryParse(XMinBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double xmin) ||
                !double.TryParse(XMaxBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double xmax) ||
                !double.TryParse(StepBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double step))
            { ErrorBox.Text = "Invalid Xmin/Xmax/Step"; return; }
            if (step <= 0 || xmax <= xmin) { ErrorBox.Text = "Ensure: Step > 0 and Xmax > Xmin."; return; }
            if (!PrepareYDefinition(out var _)) return;

            // Numeric derivative via central difference using y(x): d ~ (y(x+h)-y(x-h))/(2h)
            // Use a small h relative to the current range
            double h = Math.Max(1e-5, Math.Abs(xmax - xmin) * 1e-6);
            var pts = new List<Point>();
            double x = xmin; int guard = 0;
            while (x <= xmax + 1e-12 && guard < 200000)
            {
                string yphStr = ExprEvaluator.EvaluateExprForX("y(x)", x + h);
                if (yphStr.StartsWith("Lexer") || yphStr.StartsWith("Parser") || yphStr.StartsWith("Runtime") || yphStr.StartsWith("Error"))
                { ErrorBox.Text = $"At x={(x+h):0.###}: {yphStr}"; return; }
                string ymhStr = ExprEvaluator.EvaluateExprForX("y(x)", x - h);
                if (ymhStr.StartsWith("Lexer") || ymhStr.StartsWith("Parser") || ymhStr.StartsWith("Runtime") || ymhStr.StartsWith("Error"))
                { ErrorBox.Text = $"At x={(x-h):0.###}: {ymhStr}"; return; }
                if (!double.TryParse(yphStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double yph) ||
                    !double.TryParse(ymhStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double ymh))
                { ErrorBox.Text = $"Numeric error at x={x:0.###}"; return; }
                double d = (yph - ymh) / (2.0 * h);
                // Avoid negative zero visual
                if (Math.Abs(d) < 1e-12) d = 0.0;
                pts.Add(new Point(x, d));
                x += step; guard++;
            }
            if (pts.Count < 2) { ErrorBox.Text = "Not enough points for derivative."; return; }
            DrawPlot(pts, xmin, xmax);
            ResultTextBox.Text = "Derivative plotted";
        }

        private void Tangent_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear(); ResultTextBox.Clear();
            if (!double.TryParse(X0Box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x0))
            { ErrorBox.Text = "Enter x0"; return; }
            if (!double.TryParse(StepBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double halfW))
            { halfW = 1.0; }
            if (!PrepareYDefinition(out var _)) return;

            // Evaluate y(x0)
            string yStr = ExprEvaluator.EvaluateExprForX("y(x)", x0);
            if (!double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y0))
            { ErrorBox.Text = yStr; return; }

            // Compute numeric derivative via central difference to avoid parser/builtin edge cases
            double h = Math.Max(1e-5, Math.Abs(x0) * 1e-6 + 1e-6);
            string yphStr = ExprEvaluator.EvaluateExprForX("y(x)", x0 + h);
            if (!double.TryParse(yphStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double yph))
            { ErrorBox.Text = yphStr; return; }
            string ymhStr = ExprEvaluator.EvaluateExprForX("y(x)", x0 - h);
            if (!double.TryParse(ymhStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double ymh))
            { ErrorBox.Text = ymhStr; return; }
            double d = (yph - ymh) / (2.0 * h);
            if (Math.Abs(d) < 1e-12) d = 0.0; // normalize tiny numerical noise

            double xL = x0 - Math.Abs(halfW);
            double xR = x0 + Math.Abs(halfW);
            double yL = y0 + d * (xL - x0);
            double yR = y0 + d * (xR - x0);
            var pts = new List<Point> { new Point(xL, yL), new Point(xR, yR) };
            DrawPlot(pts, xL, xR);

            // Display normalization: avoid showing "-0" when rounding to 3 decimals
            double dDisp = Math.Round(d, 3);
            if (dDisp == 0.0) dDisp = 0.0; // force +0.0 sign
            ResultTextBox.Text = $"Tangent slope m={dDisp:0.###}";
        }

        private void Integrate_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear(); ResultTextBox.Clear();
            if (!double.TryParse(ABox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(BBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            { ErrorBox.Text = "Enter a and b"; return; }
            if (!double.TryParse(StepBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double step) || step <= 0)
            { step = 0.1; }
            if (!PrepareYDefinition(out var _)) return;
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(b - a) / step));
            string res = ExprEvaluator.EvaluateExpression($"integrate({a.ToString(CultureInfo.InvariantCulture)}, {b.ToString(CultureInfo.InvariantCulture)}, {n})");
            if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
            { ErrorBox.Text = res; return; }
            // Visualize area under curve between [a,b]
            double xmin = Math.Min(a, b);
            double xmax = Math.Max(a, b);
            var points = new List<Point>();
            double x = xmin;
            int guard = 0;
            while (x <= xmax + 1e-12 && guard < 200000)
            {
                string yStr = ExprEvaluator.EvaluateExprForX("y(x)", x);
                if (yStr.StartsWith("Lexer") || yStr.StartsWith("Parser") || yStr.StartsWith("Runtime") || yStr.StartsWith("Error"))
                { ErrorBox.Text = $"At x={x.ToString("0.###", CultureInfo.InvariantCulture)}: {yStr}"; return; }
                if (double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    && !double.IsNaN(y) && !double.IsInfinity(y))
                {
                    points.Add(new Point(x, y));
                }
                x += step;
                guard++;
            }
            if (points.Count >= 2)
            {
                DrawPlot(points, xmin, xmax, "linear", points);
            }
            ResultTextBox.Text = $"∫ y(x) dx ≈ {res}";
        }

        private void RootBisect_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear(); ResultTextBox.Clear();
            ClearCanvas(); // root finding is scalar-only: clear any previous plot
            if (!double.TryParse(ABox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(BBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            { ErrorBox.Text = "Enter a and b for bracketing"; return; }
            if (!PrepareYDefinition(out var _)) return;
            string res = ExprEvaluator.EvaluateExpression($"root_bisect({a.ToString(CultureInfo.InvariantCulture)}, {b.ToString(CultureInfo.InvariantCulture)})");
            if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
            { ErrorBox.Text = res; return; }
            ResultTextBox.Text = $"Root ≈ {res}";
        }

        private void RootNewton_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear(); ResultTextBox.Clear();
            ClearCanvas(); // scalar-only
            if (!double.TryParse(X0Box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x0))
            { ErrorBox.Text = "Enter x0"; return; }
            if (!PrepareYDefinition(out var _)) return;
            string res = ExprEvaluator.EvaluateExpression($"root_newton({x0.ToString(CultureInfo.InvariantCulture)})");
            if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
            { ErrorBox.Text = res; return; }
            ResultTextBox.Text = $"Root ≈ {res}";
        }

        private void RootSecant_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear(); ResultTextBox.Clear();
            if (!double.TryParse(ABox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x0) ||
                !double.TryParse(BBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x1))
            { ErrorBox.Text = "Enter x0 and x1"; return; }
            if (!PrepareYDefinition(out var _)) return;
            string res = ExprEvaluator.EvaluateExpression($"root_secant({x0.ToString(CultureInfo.InvariantCulture)}, {x1.ToString(CultureInfo.InvariantCulture)})");
            if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
            { ErrorBox.Text = res; return; }
            ResultTextBox.Text = $"Root ≈ {res}";
        }

        private void HelpMenu_Click(object sender, RoutedEventArgs e)
        {
            string helpText =
                    "Valid tokens and syntax:\n" +
                    "  Integers (e.g., 10)\n" +
                    "  Floats (e.g., 23.45)\n" +
                    "  Identifiers (variables/functions): start with a letter, then letters/digits/_\n" +
                    "  Operators: +  -  *  /  %  ^\n" +
                    "  Parentheses: ( )\n\n" +
                    "Statements (separate by newline or ';'):\n" 
                ;
                
            MessageBox.Show(helpText, "Help - Syntax and Tokens",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Transpile_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear();
            string source = SourceCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(source))
            {
                source = InputTextBox.Text.Trim();
            }
            if (string.IsNullOrWhiteSpace(source))
            {
                ErrorBox.Text = "Enter source code to transpile.";
                return;
            }
            try
            {
                string target = ExprEvaluator.TranspileToCSharp(source);
                TargetCodeBox.Text = target;
                ResultTextBox.Text = "Transpilation complete.";
            }
            catch (Exception ex)
            {
                ErrorBox.Text = ex.Message;
            }
        }

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear();
            string target = TargetCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                ErrorBox.Text = "No target code to compile.";
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "APCourseworkCompiler");
            Directory.CreateDirectory(tempDir);
            string srcPath = Path.Combine(tempDir, $"Program_{DateTime.Now:yyyyMMdd_HHmmss}.cs");
            string exePath = Path.ChangeExtension(srcPath, ".exe");
            File.WriteAllText(srcPath, target);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "csc",
                    Arguments = $"/nologo /t:exe /out:\"{exePath}\" \"{srcPath}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    ErrorBox.Text = "Failed to start C# compiler (csc).";
                    return;
                }
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    ErrorBox.Text = $"Compile error:\n{stderr}\n{stdout}";
                    return;
                }
                _lastCompiledPath = exePath;
                ResultTextBox.Text = "Compilation succeeded.";
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"Compile failed: {ex.Message}";
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Clear();
            if (string.IsNullOrWhiteSpace(_lastCompiledPath) || !File.Exists(_lastCompiledPath))
            {
                ErrorBox.Text = "No compiled executable available. Compile first.";
                return;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _lastCompiledPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    ErrorBox.Text = "Failed to start compiled executable.";
                    return;
                }
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    ErrorBox.Text = $"Runtime error:\n{stderr}";
                    return;
                }
                ResultTextBox.Text = string.IsNullOrWhiteSpace(stdout) ? "Program ran with no output." : stdout.Trim();
            }
            catch (Exception ex)
            {
                ErrorBox.Text = $"Run failed: {ex.Message}";
            }
        }

        // Canvas helpers
        private void ClearCanvas()
        {
            if (PlotCanvas == null) return;
            PlotCanvas.Children.Clear();
            // Keep existing pan/zoom transform intact
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
            
            ExprEvaluator.ResetState();
            foreach (var stmt in _liveSetup)
            {
                string res = ExprEvaluator.EvaluateExpression(stmt);
                if (res.StartsWith("Lexer") || res.StartsWith("Parser") || res.StartsWith("Runtime") || res.StartsWith("Error"))
                {
                    ErrorBox.Text = $"Setup error: {res}";
                    return;
                }
            }

            // enforce symmetric x-range around 0 so (0,0) is centered
            double r = Math.Max(Math.Abs(_viewXmin), Math.Abs(_viewXmax));
            if (r < 1e-9) r = 10.0;
            _viewXmin = -r;
            _viewXmax = r;

            double width = Math.Max(PlotCanvas.ActualWidth, 10);
            int targetSamples = (int)Math.Clamp(Math.Round(width / 2.5), 200, 2000);
            double xrange = Math.Max(1e-9, _viewXmax - _viewXmin);
            double step = xrange / (targetSamples - 1);

            var pts = new List<Point>(targetSamples);
            double x = _viewXmin;
            for (int i = 0; i < targetSamples; i++)
            {
                string yStr = ExprEvaluator.EvaluateExprForX(_liveExpr, x);
                if (double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    && !double.IsNaN(y) && !double.IsInfinity(y))
                {
                    pts.Add(new Point(x, y));
                }
                x += step;
            }
            if (pts.Count >= 2)
            {
                DrawPlot(pts, _viewXmin, _viewXmax);
            }
        }

        private void LivePlotCheck_Changed(object sender, RoutedEventArgs e)
        {
            _livePlotEnabled = LivePlotCheck?.IsChecked == true;
            if (!PlotCanvas?.IsLoaded ?? true) return;

            if (_livePlotEnabled)
            {
                // If live plot isn't initialized yet, attempt to initialize from current inputs
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
                        catch { /* ignore */ }
                    }
                }
                // Reset transform and replot if we have a live plot
                if (_hasLive)
                {
                    _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;
                    _translateTransform.X = 0.0; _translateTransform.Y = 0.0;
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

            // Live mode: zoom symmetrically around 0 so origin stays centered
            if (_livePlotEnabled && _hasLive)
            {
                double xrange = Math.Max(1e-9, _viewXmax - _viewXmin);
                double zoom = e.Delta > 0 ? 1.1 : 1.0 / 1.1; // wheel up -> zoom in
                double newRange = xrange / zoom;
                double r = Math.Max(newRange / 2.0, 1e-6);
                _viewXmin = -r;
                _viewXmax = r;

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

        // Embedded plotting
        private void DrawPlot(List<Point> data, double xmin, double xmax)
        {
            DrawPlot(data, xmin, xmax, "linear");
        }

        private void DrawPlot(List<Point> data, double xmin, double xmax, string mode)
        {
            DrawPlot(data, xmin, xmax, mode, null);
        }

        private void DrawPlot(List<Point> data, double xmin, double xmax, string mode, List<Point> areaPoints)
        {
            if (PlotCanvas == null) return;
            PlotCanvas.Children.Clear();
            PlotCanvas.UpdateLayout();

            double width = Math.Max(PlotCanvas.ActualWidth, 10);
            double height = Math.Max(PlotCanvas.ActualHeight, 10);
            if (width < 50) width = 800;
            if (height < 50) height = 500;
            double pad = PlotPad;

            // Symmetric ranges around 0
            double xAbsMax = Math.Max(Math.Abs(xmin), Math.Abs(xmax));
            if (xAbsMax < 1e-9) xAbsMax = 10.0;
            double sxmin = -xAbsMax;
            double sxmax = xAbsMax;

            double yAbsMax = Math.Max(Math.Abs(data.Min(p => p.Y)), Math.Abs(data.Max(p => p.Y)));
            if (yAbsMax < 1e-6) yAbsMax = 1.0;
            double ymin = -yAbsMax;
            double ymax = yAbsMax;

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

            // Area fill (optional)
            if (areaPoints != null && areaPoints.Count >= 2)
            {
                var ordered = areaPoints.OrderBy(p => p.X).ToList();
                var poly = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(80, 30, 136, 229)),
                    Stroke = Brushes.Transparent
                };
                poly.Points.Add(new System.Windows.Point(xToPx(ordered.First().X), yToPy(0)));
                foreach (var p in ordered)
                {
                    poly.Points.Add(new System.Windows.Point(xToPx(p.X), yToPy(p.Y)));
                }
                poly.Points.Add(new System.Windows.Point(xToPx(ordered.Last().X), yToPy(0)));
                PlotCanvas.Children.Add(poly);
            }

            // Plot line
            if (string.Equals(mode, "spline", StringComparison.OrdinalIgnoreCase) && data.Count >= 4)
            {
                var poly = new Polyline { Stroke = Brushes.DarkBlue, StrokeThickness = 2.5 };
                Point CR(Point p0, Point p1, Point p2, Point p3, double t)
                {
                    double t2 = t * t;
                    double t3 = t2 * t;
                    double x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                    double y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                    return new Point(x, y);
                }
                var pts = data.ToList();
                pts.Sort((a, b) => a.X.CompareTo(b.X));
                pts.Insert(0, pts[0]);
                pts.Add(pts[pts.Count - 1]);
                for (int i = 0; i < pts.Count - 3; i++)
                {
                    for (double t = 0; t <= 1.0; t += 0.05)
                    {
                        var c = CR(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], t);
                        var sx = xToPx(c.X);
                        var sy = yToPy(c.Y);
                        poly.Points.Add(new System.Windows.Point(sx, sy));
                    }
                }
                PlotCanvas.Children.Add(poly);
            }
            else
            {
                var poly = new Polyline { Stroke = Brushes.DarkBlue, StrokeThickness = 2.5 };
                foreach (var p in data.OrderBy(p => p.X))
                {
                    poly.Points.Add(new System.Windows.Point(xToPx(p.X), yToPy(p.Y)));
                }
                PlotCanvas.Children.Add(poly);
            }

        }

    }
}
