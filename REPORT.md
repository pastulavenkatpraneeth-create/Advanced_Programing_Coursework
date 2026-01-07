# Advanced Programming Coursework Report

## Abstract
This report documents the design and implementation of a desktop mathematical expression interpreter with a WPF-based GUI and an F# evaluation core. The system supports arithmetic expressions, variables, user-defined single-argument functions, plotting of functions of one variable, and a selection of numerical methods (differentiation, integration, root finding). It also includes a lightweight transpiler that maps the custom expression language to C# for compilation and execution. The report outlines the architecture, language design, parsing strategy, evaluation model, GUI integration, and selected extensions.

## 1. Introduction
The coursework requires a desktop mathematics application that combines a graphical user interface (C# WPF) with an interpreter core (F#). The primary objectives are to evaluate arithmetic expressions, support variable assignment and usage, and visualize functions in a plotting area. The system is developed as a single Visual Studio solution and adheres to manual lexing/parsing rather than compiler-compiler tools.

This report describes the implementation in the repository, focusing on the interpreter pipeline, GUI interactions, and extensions such as numerical methods and a basic transpiler.

## 2. System Architecture
The solution is composed of two projects:

- **Interpreter core (F#)**: Implements tokenization, parsing, evaluation, symbol/function tables, and auxiliary numerical routines.
- **GUI (C# WPF)**: Provides input, output, error reporting, plotting canvas, and optional compiler/transpiler controls.

### 2.1 Data Flow
1. User enters expressions/definitions in the GUI.
2. The GUI forwards input to the interpreter for evaluation or prepares a function definition (e.g., `y(x)` for plotting).
3. The interpreter returns a computed value or a diagnostic error string.
4. For plotting, the GUI samples the function across an interval and draws the resulting polyline.

## 3. Language Specification
The expression language supports:

- **Numeric literals**: integers and floating-point numbers (including exponent notation).
- **Operators**: `+`, `-`, `*`, `/`, `%`, `^` with standard precedence rules and left associativity.
- **Unary operators**: `+` and `-`.
- **Grouping**: parentheses.
- **Assignments**: `x = 10`.
- **User-defined functions**: `f(x) = x^2 + 1`.
- **Control flow**: a simple `for` loop form for repeated evaluation.
- **Built-in functions**: trigonometric and common math functions, plus numerical utilities.

The grammar is encoded in a recursive descent parser, preserving BODMAS/BIDMAS rules with explicit precedence tiers for expressions, terms, factors, unary expressions, and primaries.

## 4. Lexer Design
Tokenization is implemented by scanning character sequences into a set of tokens that represent operators, delimiters, identifiers, and numbers. The lexer distinguishes identifiers from numeric literals and recognizes floating-point formats. Lexical errors are surfaced with explicit diagnostic messages.

Key features include:
- Support for floating-point literals with decimal points.
- Exponent notation (e.g., `1.2e-3`) to align with numeric expectations in scientific contexts.

## 5. Parser and Evaluator
### 5.1 Parsing Strategy
The parser uses a recursive descent approach, mapping closely to the grammar rules. Each parsing function corresponds to a non-terminal in the grammar and returns a value alongside information about numeric type handling and remaining tokens.

### 5.2 Evaluation Model
Evaluation is performed during parsing (expression-evaluation parsing). The interpreter maintains:

- A **symbol table** for variables.
- A **function table** for user-defined functions (`f(x) = ...`).

Errors are reported as lexer, parser, or runtime errors with clear messages, including division-by-zero detection. Integer-like values are normalized to prevent floating-point artifacts in output when appropriate.

## 6. GUI Design and Features
The WPF GUI provides:

- **Input**: multiline expression/definition text box.
- **Output**: result display and error display.
- **Plotting canvas**: draws sampled points for `y(x)` using a configurable range and step size.
- **Interaction**: pan/zoom adjustments and grid/axis rendering for readability.

The plotting system supports basic line rendering and can optionally approximate curves with spline interpolation. The GUI also includes controls for numerical methods (derivative, tangent line, integration, root finding) to visualize or compute analytical properties.

## 7. Numerical Extensions
The system includes:

- **Numeric differentiation** using central difference.
- **Trapezoidal integration** over a given interval.
- **Root finding** using bisection, Newton-Raphson, and secant methods.
- **Vector/matrix helpers**: scalar outputs from dot product, norm, and determinant computations.

These extensions extend the basic language without requiring a separate data-type system, as values are represented as floating-point numbers in the evaluator.

## 8. Transpiler and Compiler Flow
A basic transpiler maps expression programs to C# code. The intent is to demonstrate a simple compilation pipeline:

- Parse expressions using the same grammar structure.
- Emit equivalent C# expressions and helper functions.
- Generate a C# program wrapper with a `Main` entry point.

The GUI can invoke the transpiler, compile the produced C# with the system compiler, and run the resulting executable. This implements a rudimentary compiler workflow for the custom language without adding a full code generation or optimization pipeline.

## 9. Limitations and Constraints
- The transpiler focuses on a subset of language features and excludes certain interpreter-specific built-ins (e.g., plotting).
- The system uses numeric evaluation rather than symbolic algebra, so differentiation/integration are numerical rather than exact.
- Advanced type systems and complex number support are not implemented.

## 10. Conclusion
The project meets the core objectives of building an interpreter-based mathematical application with GUI integration. It demonstrates a complete pipeline from lexical analysis to evaluation, integrates visual plotting in a desktop environment, and extends the language with numerical methods and a simple transpiler. The modular separation of the interpreter and GUI supports maintainability and aligns with software engineering principles such as cohesion and separation of concerns.

## Appendix A: Running the Application
1. Open the solution in Visual Studio.
2. Build the solution.
3. Run the WPF project.
4. Enter expressions or function definitions in the input panel.
5. Use Evaluate/Plot and other buttons to compute values and visualize functions.

## Appendix B: Example Inputs
- Arithmetic: `1 + 2 * 3`
- Variables: `x = 10; x * 2`
- Function: `y(x) = x^2 - 4*x + 3`
- Plot: enter `y(x)` definition and click Plot
- Integration: define `y(x)` and use Integrate with bounds
