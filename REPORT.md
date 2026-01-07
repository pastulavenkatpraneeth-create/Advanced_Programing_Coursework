# Advanced Programming Coursework Report

**Project:** Desktop Mathematical Interpreter and Visualiser  \
**Languages:** F# (interpreter) and C# (WPF GUI)  \
**Date:** 2025–2026 Academic Year

---

## Abstract
This report documents the design, implementation, and evaluation of a desktop mathematics application comprising an interpreter written in F# and a graphical user interface (GUI) written in C# with WPF. The system evaluates arithmetic expressions, supports variable assignment, user-defined functions of one variable, and provides interactive plotting of functions. It extends core functionality with numerical differentiation, integration, and root finding, and includes a basic transpiler that maps the custom language to C# to demonstrate a compiler-like workflow. The report presents the language design, parsing strategy, evaluation model, error handling approach, and GUI architecture; discusses the numerical methods implemented; and reflects on limitations and future work. The work demonstrates a complete pipeline from lexical analysis to evaluation and visualisation, aligning with software engineering principles such as modularity and separation of concerns.

---

## Table of Contents
1. Introduction
2. Requirements and Scope
3. System Architecture
4. Language Design and Grammar
5. Lexer Design
6. Parser and Evaluation Model
7. Symbol and Function Tables
8. Error Handling and Diagnostics
9. GUI Architecture and Interaction Design
10. Plotting and Visualisation Pipeline
11. Numerical Extensions
12. Transpiler and Compiler Workflow
13. Testing and Validation
14. Performance Considerations
15. Code Structure and Maintainability
16. Limitations
17. Future Work
18. Conclusion
19. Appendix A: Example Scripts
20. Appendix B: Build and Run Instructions

---

## 1. Introduction
The goal of this project is to build a desktop mathematics application that integrates two programming paradigms: a functional interpreter core in F# and an object-oriented GUI in C# using WPF. The coursework objectives include constructing a custom interpreter capable of evaluating arithmetic expressions, handling variables, and extending the language to support plotting functions. The project emphasizes modern software engineering practices such as modular design, clear separation of concerns, and explicit error handling.

This report provides a detailed technical account of the implemented system. The interpreter follows a classic pipeline: lexical analysis transforms text into tokens, a recursive-descent parser enforces precedence and associativity, and evaluation is performed during parsing. The GUI collects user input, displays results and errors, and renders visual plots of functions. Additional numerical and compiler-like features provide a richer demonstration of the system’s extensibility.

---

## 2. Requirements and Scope
The specification requires a desktop application with the following essential features:

- **Arithmetic expression evaluation** using integers and floats.
- **Variable assignment and usage** within expressions.
- **GUI** with input and output fields for expressions and results.
- **Plotting** of functions of one variable in a dedicated plotting area.

The interpreter must be implemented in F#, the GUI in C# WPF, and automated parser generators are disallowed. Optional features include numerical methods, function definitions, and compiler-like support.

The scope of this implementation includes:

- Full arithmetic evaluation with BODMAS/BIDMAS precedence and left associativity.
- Variable assignment and a symbol table.
- Function definition of single-variable functions (e.g., `y(x)`), stored in a function table.
- Plotting of functions `y(x)` through sampling.
- Numerical methods: differentiation, integration (trapezoidal), and root finding (bisection/Newton/secant).
- A basic transpiler that generates equivalent C# from interpreter scripts.

---

## 3. System Architecture
The solution is organised into two projects within a single Visual Studio solution:

1. **Interpreter Core (F#)**
   - Implements lexical analysis, parsing, evaluation, symbol and function tables.
   - Exposes a simple API for the GUI: evaluation, plotting data buffers, and transpilation.

2. **GUI (C# WPF)**
   - Manages user input, output, and visualisation.
   - Coordinates plotting and numerical operations.
   - Hosts optional compiler/transpiler controls.

### 3.1 Architectural Rationale
A split architecture is chosen to maximise cohesion and reduce coupling. The interpreter remains GUI-agnostic, enabling reuse and unit testing independent of UI. The GUI is responsible for presentation and interaction but does not implement parsing or evaluation logic.

### 3.2 Component Interaction
The primary interaction is a call–response flow:

- User submits expressions in the GUI.
- The GUI sends the input string to the interpreter.
- The interpreter returns a result or an error string.
- The GUI displays the output and, for plotting, samples values using interpreter evaluation at specified x-values.

---

## 4. Language Design and Grammar
The language is designed to be compact, human-readable, and mathematically expressive. It supports both statements (assignments, function definitions) and expressions. The grammar is implemented in the interpreter and is expressed informally below:

```
<statement>    ::= <functiondef> | <assignment> | <forloop> | <expression>
<functiondef>  ::= <identifier> "(" <identifier> ")" "=" <expression>
<assignment>   ::= <identifier> "=" <expression>
<forloop>      ::= "for" <identifier> "=" <expression> "to" <expression> ["step" <expression>] "do" <expression>
<expression>   ::= <term> { ("+" | "-") <term> }
<term>         ::= <factor> { ("*" | "/" | "%") <factor> }
<factor>       ::= <unary> [ "^" <factor> ]
<unary>        ::= ["+"|"-"] <unary> | <primary>
<primary>      ::= <number>
                 | <identifier>
                 | <identifier> "(" <arglist> ")"
                 | "(" <expression> ")"
<arglist>      ::= <expression> { "," <expression> }
```

### 4.1 Operator Precedence
- Highest: parenthesis, function calls.
- Next: exponentiation `^` (right-associative).
- Next: unary `+`/`-`.
- Next: multiplication/division/modulo.
- Lowest: addition/subtraction.

### 4.2 Numeric Literals
Numeric literals include integers, decimal floats, and exponent notation (e.g., `1.2e-3`). Exponent support improves usability for scientific computation and aligns with common calculator syntax.

---

## 5. Lexer Design
The lexer processes raw input into a list of tokens. It handles:

- **Whitespace** skipping.
- **Single-character tokens**: operators, parentheses, assignment, comma.
- **Numbers**: digit sequences with optional decimal point and optional exponent marker.
- **Identifiers**: letter-initiated strings that may include digits and underscores.

The lexer reports a `LexError` when encountering invalid characters or malformed numeric literals.

### 5.1 Exponent Parsing
Exponent notation is recognised when a number contains an `E` or `e` followed by an optional sign and digits. This supports values like `1e6` and `3.2E-4`. The lexer treats such tokens as floating values.

---

## 6. Parser and Evaluation Model
The parser is a recursive-descent parser with evaluation performed directly during parsing. This eliminates the need to build explicit abstract syntax trees for core evaluation tasks, simplifying the interpreter for this scope.

### 6.1 Expression Evaluation
Each parsing function returns:
- the remaining tokens,
- a numeric value,
- a flag indicating whether floating arithmetic is required.

This allows integer-like results to be formatted appropriately while preserving floating-point semantics.

### 6.2 Function Calls
Identifiers followed by parentheses are treated as function calls. These can be user-defined functions stored in the function table, or built-ins such as `sin`, `cos`, `log`, and numerical helpers (e.g., `dot`, `norm`).

### 6.3 Control Flow: For Loop
A lightweight `for` loop form enables repeated evaluation over a range and is useful for plotting and iterative computations. The loop is implemented directly within the evaluator, updating the symbol table for each iteration.

---

## 7. Symbol and Function Tables
The interpreter maintains two mutable maps:

- **Symbol table**: maps variable names to values.
- **Function table**: maps function names to parameter name and token list for the body.

This design enables simple variable usage and function application without introducing a full static type system. The tables are reset between evaluation sessions when necessary (e.g., during plotting or transpiler setup).

---

## 8. Error Handling and Diagnostics
Errors are categorised into:

- **Lexical errors**: invalid characters or malformed numbers.
- **Parser errors**: unexpected token sequences or missing delimiters.
- **Runtime errors**: division by zero, undefined variables, or invalid numeric operations.

All errors are propagated as user-friendly strings for display in the GUI. This ensures that erroneous input does not crash the application and provides clear feedback for debugging.

---

## 9. GUI Architecture and Interaction Design
The GUI is a WPF application that includes:

- Input box for expressions and definitions.
- Output box for results.
- Error box for diagnostic messages.
- Plotting canvas for visual output.
- Controls for plotting range and step size.
- Buttons for derivative, tangent, integration, and root finding.
- Optional compiler/transpiler panel.

The GUI follows an event-driven architecture, with each button click invoking a handler that delegates computation to the interpreter.

---

## 10. Plotting and Visualisation Pipeline
Plotting is achieved by sampling `y(x)` across a defined interval. The GUI collects user-specified values (`Xmin`, `Xmax`, `Step`), evaluates `y(x)` at each sample point using the interpreter, and draws the resulting curve on a `Canvas`.

### 10.1 Coordinate Mapping
Plot points are translated into screen coordinates using a mapping function that scales and shifts values to fit within the canvas, including padding. The x- and y-axes are drawn through the origin for mathematical clarity.

### 10.2 Grid and Axis Rendering
Grid lines and ticks are rendered for readability. A “nice step” algorithm chooses tick spacing based on the current range, improving visual clarity regardless of zoom level.

### 10.3 Integration Shading
When computing integrals, the area under the curve is shaded by creating a polygon from the sampled points plus the baseline. This provides a visual representation of the numerical integration result.

---

## 11. Numerical Extensions
The system includes several numerical algorithms commonly used in scientific computing:

### 11.1 Differentiation
Differentiation is implemented using a central difference approximation:

```
 f'(x) ≈ (f(x+h) - f(x-h)) / (2h)
```

The step size `h` is selected relative to the plotting range to balance accuracy and numerical stability.

### 11.2 Integration
Integration is performed via the trapezoidal rule:

```
∫_a^b f(x) dx ≈ (h/2) [f(a) + 2 Σ f(a + i·h) + f(b)]
```

This method is robust for smooth functions and pairs naturally with sampled plotting data.

### 11.3 Root Finding
Root finding methods include:

- **Bisection**: guaranteed convergence with sign change constraints.
- **Newton-Raphson**: faster convergence but requires derivative estimation.
- **Secant**: derivative-free variant of Newton’s method.

Each method is parameterised with tolerance and iteration limits to prevent runaway computations.

---

## 12. Transpiler and Compiler Workflow
The transpiler maps interpreter scripts to valid C# code using a simplified parsing pipeline that mirrors the interpreter’s expression grammar. The output includes helper functions for dot products, norms, and determinants.

### 12.1 Supported Constructs
- Assignments and expressions.
- Single-argument user-defined functions.
- `for` loops in the custom syntax, mapped to C# `for` loops.

### 12.2 Generated Output
The transpiler produces a complete C# program with a `Main()` entry point and helper methods. The GUI can invoke a system C# compiler (`csc`) to compile the output and run the generated executable.

### 12.3 Limitations
Not all interpreter built-ins are mapped to C#. Plotting and numerical methods that depend on interpreter context are excluded. This is consistent with the transpiler’s role as a proof-of-concept rather than a full compiler.

---

## 13. Testing and Validation
Testing is performed manually via the GUI, focusing on correctness of evaluation and plotting. Representative tests include:

- Arithmetic precedence: `1 + 2 * 3` should yield `7`.
- Variable assignment: `x = 10; x * 2` should yield `20`.
- Function plotting: `y(x) = x^2` should display a parabola.
- Numerical methods: integration and root finding should yield plausible values for polynomial inputs.

While automated tests are not included in the repository, the modular design of the interpreter enables future unit tests for lexer, parser, and evaluator components.

---

## 14. Performance Considerations
The interpreter is efficient for the intended educational scope, but sampling-based plotting can generate a large number of evaluation calls for dense plots. The GUI includes guard limits to prevent excessive loops. For more advanced performance needs, caching or vectorised evaluation could be introduced.

---

## 15. Code Structure and Maintainability
The codebase is organised into two top-level project folders, each with clear responsibilities. The interpreter code is structured into logical sections (lexer, parser, evaluation), with explicit data structures for symbol and function tables. The GUI code separates user input handling from rendering logic.

This modular organisation supports maintainability and makes it easier for a team to extend the system with new features.

---

## 16. Limitations
Despite meeting key objectives, the system has constraints:

- The language is untyped and uses floating-point values universally, limiting exact integer semantics.
- The transpiler is basic and does not support all built-ins.
- Plotting focuses on 2D functions of one variable and does not support surfaces or multivariate functions.

These limitations are acceptable given the project’s timeframe and learning outcomes.

---

## 17. Future Work
Potential future improvements include:

- Extending the language to support multi-variable functions.
- Adding a static type system for integers and floats.
- Implementing symbolic differentiation and integration.
- Improving transpiler coverage and adding optimisations.
- Introducing automated tests and CI integration.

---

## 18. Conclusion
This project delivers a functional desktop mathematics application with an interpreter core and a WPF GUI. It demonstrates key language-processing concepts, integrates numerical methods, and includes a basic transpiler workflow. The design is modular and extensible, providing a strong foundation for further development.

---

## 19. Appendix A: Example Scripts

### A.1 Arithmetic and Variables
```
1 + 2 * 3
x = 10
x * 2
```

### A.2 Function Definition
```
y(x) = x^2 - 4*x + 3
```

### A.3 Root Finding
```
y(x) = x^2 - 4
root_bisect(-10, 10)
```

---

## 20. Appendix B: Build and Run Instructions

1. Open the solution file in Visual Studio.
2. Build the solution to compile the F# and C# projects.
3. Run the GUI project.
4. Use the input box to enter expressions and plot functions.
5. Use the optional compiler panel to transpile and compile scripts.
