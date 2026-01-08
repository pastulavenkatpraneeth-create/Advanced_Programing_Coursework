module ExprEvaluator
open System
open System.IO
open System.Text

// Tokens
type Token =
  | Plus | Minus | Mul | Div | Mod | Pow
  | Lpar | Rpar | Assign | Comma
  | LBracket | RBracket | Semicolon
  | Number of float * bool
  | Ident of string

exception LexError of string
exception ParseError of string
exception EvalError of string

//BNF (extended)
//<program>      ::= <statement>
//<statement>    ::= <functiondef> | <assignment> | <forloop> | <expression>
//<functiondef>  ::= <identifier> "(" <identifier> ")" "=" <expression>
//<assignment>   ::= <identifier> "=" <expression>
//<forloop>      ::= "for" <identifier> "=" <expression> "to" <expression> ["step" <expression>] "do" <expression>
//<expression>   ::= <term> { ("+" | "-") <term> }
//<term>         ::= <factor> { ("*" | "/" | "%") <factor> }
//<factor>       ::= <unary> [ "^" <factor> ]
//<unary>        ::= ["+"|"-"] <unary> | <primary>
//<primary>      ::= <number>
//                 | <identifier>
//                 | <identifier> "(" <arglist> ")"   // user funcs (1 arg) and built-ins (var-args)
//                 | "(" <expression> ")"
//<arglist>      ::= <expression> { "," <expression> }
//<number>       ::= <digit> { <digit> } [ "." <digit> { <digit> } ]
//<identifier>   ::= <letter> { <letter> | <digit> | "_" }
//
//Built-ins (operate on current y(x)):
//  plot(x, dx, mode)
//  diff(x0[, h])
//  tangent(x0, halfWidth[, mode])
//  integrate(a, b[, n])
//  root_bisect(a, b[, tol][, maxIter])
//  root_newton(x0[, tol][, maxIter])
//  root_secant(x0, x1[, tol][, maxIter])
//
//Vector/Matrix helpers (scalar results):
//  dot(a1,b1[, a2,b2, ...])
//  norm(a1[, a2, ...])
//  det(a,b,c,d)                 // 2x2 determinant |a b; c d|
//  det(a11,a12,...,a33)         // 3x3 determinant (row-major)


// Lexer

let isDigit c = Char.IsDigit c
let isLetter c = Char.IsLetter c
let isWhite c = Char.IsWhiteSpace c
let isIdentChar c = Char.IsLetterOrDigit c || c = '_'

let rec scanNumber chars acc =
  // Scans: digits [ '.' digits ] [ ( 'e' | 'E' ) [ '+' | '-' ] digits ]
  let rec scanIntPart cs ac =
    match cs with
    | c :: tail when isDigit c -> scanIntPart tail (ac + string c)
    | '.' :: tail when not (ac.Contains ".") -> scanFracPart tail (ac + ".")
    | 'e' :: tail | 'E' :: tail -> scanExpPart tail (ac + "e")
    | _ -> cs, ac
  and scanFracPart cs ac =
    match cs with
    | c :: tail when isDigit c -> scanFracPart tail (ac + string c)
    | 'e' :: tail | 'E' :: tail -> scanExpPart tail (ac + "e")
    | _ -> cs, ac
  and scanExpPart cs ac =
    match cs with
    | '+' :: tail -> scanExpDigits tail (ac + "+")
    | '-' :: tail -> scanExpDigits tail (ac + "-")
    | _ -> scanExpDigits cs ac
  and scanExpDigits cs ac =
    match cs with
    | c :: tail when isDigit c -> scanExpDigits tail (ac + string c)
    | _ -> cs, ac
  scanIntPart chars acc

let rec scanIdent chars acc =
  match chars with
  | c :: tail when isIdentChar c -> scanIdent tail (acc + string c)
  | _ -> chars, acc

let lexer (input: string) : Token list =
  let rec scan cs =
    match cs with
    | [] -> []
    | c :: tail when isWhite c -> scan tail
    | '+' :: tail -> Plus :: scan tail
    | '-' :: tail -> Minus :: scan tail
    | '*' :: tail -> Mul :: scan tail
    | '/' :: tail -> Div :: scan tail
    | '%' :: tail -> Mod :: scan tail
    | '^' :: tail -> Pow :: scan tail
    | '(' :: tail -> Lpar :: scan tail
    | ')' :: tail -> Rpar :: scan tail
    | '=' :: tail -> Assign :: scan tail
    | ',' :: tail -> Comma :: scan tail
    | '[' :: tail -> LBracket :: scan tail
    | ']' :: tail -> RBracket :: scan tail
    | ';' :: tail -> Semicolon :: scan tail
    | c :: _ when isLetter c ->
        let (rest, idStr) = scanIdent cs ""
        Ident idStr :: scan rest
    | c :: _ when isDigit c ->
        let (rest, numStr) = scanNumber cs ""
        match System.Double.TryParse numStr with
        | true, v ->
            let hadDot = numStr.Contains "."
            Number (v, hadDot) :: scan rest
        | _ -> raise (LexError $"Invalid number: {numStr}")
    | other :: _ -> raise (LexError $"Unknown character: '{other}'")
  scan (List.ofSeq input)

let mutable symbolTable = Map.empty<string, float>
let mutable functionTable = Map.empty<string, string * Token list>

// Plotting state for GUI integration
let mutable plotPoints : (float * float) list = []
let mutable plotMode : string option = None
let mutable plotRange : (float * float) option = None

let mutable inExponentContext = false

let rec pow baseF expF =
  // Support integer and fractional exponents with domain checks
  let isInt (x: float) = Math.Abs(x - Math.Round(x)) < 1e-12
  if expF = 0.0 then 1.0
  elif expF = 1.0 then baseF
  elif isInt expF then
    // Fast integer exponent (handles negative exponent too)
    let n = int (Math.Round expF)
    if n >= 0 then
      let rec loop acc k = if k <= 0 then acc else loop (acc * baseF) (k - 1)
      loop 1.0 n
    else
      1.0 / (pow baseF (float -n))
  else
    // Fractional exponent
    if baseF < 0.0 then raise (EvalError "Fractional power: negative base is not allowed")
    else Math.Pow(baseF, expF)

let absf x = if x >= 0.0 then x else -x

let isClose a b = 
  let diff = if a > b then a - b else b - a
  diff < 1e-10

let isIntLike f =
  let n = int f
  isClose f (float n)

let intDiv a b =
  if b = 0.0 then raise (EvalError "Division by zero")
  else
    // Truncate toward zero, like typical integer division in many languages
    let q = a / b
    float (Math.Truncate q)

let intMod a b =
  if b = 0.0 then raise (EvalError "Modulo by zero")
  else a - b * intDiv a b

// Parser

// --- Symbolic derivative support (LUT-based) ---

type SExpr =
  | SNum of float
  | SVar
  | SNeg of SExpr
  | SAdd of SExpr * SExpr
  | SSub of SExpr * SExpr
  | SMul of SExpr * SExpr
  | SDiv of SExpr * SExpr
  | SPow of SExpr * SExpr
  | SSin of SExpr
  | SCos of SExpr
  | STan of SExpr
  | SExp of SExpr
  | SLog of SExpr

let rec parseSPrimary param tokens =
  match tokens with
  | Number (n, _) :: tail -> Some (SNum n, tail)
  | Ident name :: Lpar :: tail ->
      // support only unary functions for symbolic parsing
      let nameL = name.ToLowerInvariant()
      let opt = parseSExpr param tail
      match opt with
      | Some (arg, rest1) ->
          match rest1 with
          | Rpar :: rest2 ->
              let node =
                match nameL with
                | "sin" -> Some (SSin arg)
                | "cos" -> Some (SCos arg)
                | "tan" -> Some (STan arg)
                | "exp" -> Some (SExp arg)
                | "log" -> Some (SLog arg)
                | _ -> None
              match node with
              | Some n -> Some (n, rest2)
              | None -> None
          | _ -> None
      | None -> None
  | Ident name :: tail when name = param -> Some (SVar, tail)
  | Lpar :: tail ->
      match parseSExpr param tail with
      | Some (e, rest) ->
          match rest with | Rpar :: rest2 -> Some (e, rest2) | _ -> None
      | None -> None
  | _ -> None

and parseSUnary param tokens =
  match tokens with
  | Minus :: tail ->
      match parseSUnary param tail with
      | Some (e, rest) -> Some (SNeg e, rest)
      | None -> None
  | _ -> parseSPrimary param tokens

and parseSPower param tokens =
  // left-assoc '^'
  let rec loop acc toks =
    match toks with
    | Pow :: tail ->
        match parseSUnary param tail with
        | Some (rhs, rest) -> loop (SPow (acc, rhs)) rest
        | None -> None
    | _ -> Some (acc, toks)
  match parseSUnary param tokens with
  | Some (u, rest) -> loop u rest
  | None -> None

and parseSTerm param tokens =
  let rec loop acc toks =
    match toks with
    | Mul :: tail ->
        match parseSPower param tail with
        | Some (rhs, rest) -> loop (SMul (acc, rhs)) rest
        | None -> None
    | Div :: tail ->
        match parseSPower param tail with
        | Some (rhs, rest) -> loop (SDiv (acc, rhs)) rest
        | None -> None
    | _ -> Some (acc, toks)
  match parseSPower param tokens with
  | Some (f, rest) -> loop f rest
  | None -> None

and parseSExpr param tokens =
  let rec loop acc toks =
    match toks with
    | Plus :: tail ->
        match parseSTerm param tail with
        | Some (rhs, rest) -> loop (SAdd (acc, rhs)) rest
        | None -> None
    | Minus :: tail ->
        match parseSTerm param tail with
        | Some (rhs, rest) -> loop (SSub (acc, rhs)) rest
        | None -> None
    | _ -> Some (acc, toks)
  match parseSTerm param tokens with
  | Some (t, rest) -> loop t rest
  | None -> None

let rec sdiff expr =
  match expr with
  | SNum _ -> SNum 0.0
  | SVar -> SNum 1.0
  | SNeg u -> SNeg (sdiff u)
  | SAdd (a,b) -> SAdd (sdiff a, sdiff b)
  | SSub (a,b) -> SSub (sdiff a, sdiff b)
  | SMul (u,v) -> SAdd (SMul (sdiff u, v), SMul (u, sdiff v))
  | SDiv (u,v) -> SDiv (SSub (SMul (sdiff u, v), SMul (u, sdiff v)), SPow (v, SNum 2.0))
  | SPow (u, v) ->
      match v with
      | SNum n -> SMul (SMul (SNum n, SPow (u, SNum (n - 1.0))), sdiff u)
      | _ ->
          // d(u^v) = u^v * (v' * log u + v * u'/u)
          // domain of log(u) left to runtime
          let term1 = SMul (sdiff v, SLog u)
          let term2 = SDiv (SMul (v, sdiff u), u)
          SMul (SPow (u, v), SAdd (term1, term2))
  | SSin u -> SMul (SCos u, sdiff u)
  | SCos u -> SNeg (SMul (SSin u, sdiff u))
  | STan u -> SDiv (sdiff u, SPow (SCos u, SNum 2.0))
  | SExp u -> SMul (SExp u, sdiff u)
  | SLog u -> SDiv (sdiff u, u)

let rec seval x expr =
  match expr with
  | SNum n -> n
  | SVar -> x
  | SNeg u -> - (seval x u)
  | SAdd (a,b) -> (seval x a) + (seval x b)
  | SSub (a,b) -> (seval x a) - (seval x b)
  | SMul (a,b) -> (seval x a) * (seval x b)
  | SDiv (a,b) -> (seval x a) / (seval x b)
  | SPow (a,b) -> Math.Pow(seval x a, seval x b)
  | SSin u -> Math.Sin (seval x u)
  | SCos u -> Math.Cos (seval x u)
  | STan u -> Math.Tan (seval x u)
  | SExp u -> Math.Exp (seval x u)
  | SLog u -> Math.Log (seval x u)
let rec parseE tokens =
  let (tokens2, v1, hasFloat1) = parseT tokens
  parseEopt tokens2 v1 hasFloat1

and parseEopt tokens acc hasFloat =
  match tokens with
  | Plus :: tail ->
      let (t2, v2, f2) = parseT tail
      parseEopt t2 (acc + v2) (hasFloat || f2)
  | Minus :: tail ->
      let (t2, v2, f2) = parseT tail
      parseEopt t2 (acc - v2) (hasFloat || f2)
  | _ -> tokens, acc, hasFloat

and parseT tokens =
  let (tokens2, v1, f1) = parseF tokens
  parseTopt tokens2 v1 f1

and parseTopt tokens acc hasFloat =
  match tokens with
  | Mul :: tail ->
      let (t2, v2, f2) = parseF tail
      parseTopt t2 (acc * v2) (hasFloat || f2)
  | Div :: tail ->
      let (t2, v2, f2) = parseF tail
      if v2 = 0.0 then raise (EvalError "Division by zero")
      let result =
        if inExponentContext then acc / v2
        // Use integer division ONLY if both sides are int-like AND neither side came from a float context
        elif (not hasFloat) && (not f2) && (isIntLike acc) && (isIntLike v2) then intDiv acc v2
        else acc / v2
      // Update float flag: division yields float if any side is float-like or in exponent context
      parseTopt t2 result (hasFloat || f2 || inExponentContext || not (isIntLike acc && isIntLike v2))
  | Mod :: tail ->
      let (t2, v2, f2) = parseF tail
      let result = intMod acc v2
      parseTopt t2 result (hasFloat || f2)
  | _ -> tokens, acc, hasFloat

and parseF tokens =
  // Left-associative power: (((a ^ b) ^ c) ^ d)
  let (t2, v1, f1) = parseU tokens
  let rec loop acc accF toks =
    match toks with
    | Pow :: tail ->
        let prevCtx = inExponentContext
        inExponentContext <- true
        let (tNext, v2, f2) = parseU tail
        inExponentContext <- prevCtx
        let res = pow acc v2
        let resF = true // power generally yields float domain; treat as float to avoid integer artifacts
        loop res resF tNext
    | _ -> toks, acc, accF
  loop v1 f1 t2

and parseU tokens =
  match tokens with
  | Minus :: tail ->
      let (ts2, v, f) = parseU tail
      ts2, -v, f
  | Plus :: tail -> parseU tail
  | _ -> parseP tokens

and parseP tokens =
  // Helpers for new built-ins
  let evalYAt xVal =
    match functionTable.TryFind "y" with
    | Some (param, body) ->
        let oldBinding = symbolTable.TryFind param
        symbolTable <- symbolTable.Add(param, xVal)
        let (afterBody, result, resFloat) = parseE body
        // Body should fully consume tokens
        if afterBody <> [] then
            match oldBinding with
            | Some v -> symbolTable <- symbolTable.Add(param, v)
            | None -> symbolTable <- symbolTable.Remove param
            raise (ParseError "Extra tokens in function body of y")
        // restore
        match oldBinding with
        | Some v -> symbolTable <- symbolTable.Add(param, v)
        | None -> symbolTable <- symbolTable.Remove param
        result
    | None -> raise (EvalError "Undefined function: y(x) not defined")
  // Parse a comma-separated argument list into floats until ')'
  let rec parseArgs acc toks =
    let (t1, v, _) = parseE toks
    let acc2 = v :: acc
    match t1 with
    | Comma :: t2 -> parseArgs acc2 t2
    | Rpar :: rest -> List.rev acc2, rest
    | _ -> raise (ParseError "Expected ',' or ')' in argument list")
  match tokens with
  | Number (n, hadDot) :: tail -> tail, n, (hadDot || not (isIntLike n))
  // Vector/Matrix helpers and shading
  | Ident "dot" :: Lpar :: tail ->
      let (args, rest) = parseArgs [] tail
      if args.Length < 2 || args.Length % 2 <> 0 then raise (EvalError "dot expects an even number of arguments >= 2: dot(a1,b1[,a2,b2,...])")
      let half = args.Length / 2
      let mutable sum = 0.0
      for i in 0 .. half - 1 do
        sum <- sum + args[i] * args[i + half]
      rest, sum, true
  | Ident "norm" :: Lpar :: tail ->
      let (args, rest) = parseArgs [] tail
      if args.Length < 1 then raise (EvalError "norm expects at least 1 argument")
      let mutable s = 0.0
      for v in args do s <- s + v * v
      rest, Math.Sqrt(s), true
  | Ident "det" :: Lpar :: tail ->
      let (args, rest) = parseArgs [] tail
      match args.Length with
      | 4 ->
          let a,b,c,d = args[0], args[1], args[2], args[3]
          rest, (a*d - b*c), true
      | 9 ->
          let a11,a12,a13,a21,a22,a23,a31,a32,a33 = args[0],args[1],args[2],args[3],args[4],args[5],args[6],args[7],args[8]
          let det3 = a11*(a22*a33 - a23*a32) - a12*(a21*a33 - a23*a31) + a13*(a21*a32 - a22*a31)
          rest, det3, true
      | _ -> raise (EvalError "det expects 4 (2x2) or 9 (3x3) arguments")
  | Ident "shade" :: Lpar :: tail ->
      // shade(a,b) marks an interval for GUI area fill; returns length (b-a)
      let (t1, a, _) = parseE tail
      match t1 with
      | Comma :: t2 ->
          let (t3, b, _) = parseE t2
          match t3 with
          | Rpar :: rest ->
              let aa, bb = if a <= b then a, b else b, a
              plotRange <- Some (aa, bb)
              rest, (bb - aa), true
          | _ -> raise (ParseError "Expected ')' in shade")
      | _ -> raise (ParseError "Expected comma in shade arguments")
  | Ident "plot" :: Lpar :: tail ->
      // parse plot(x, dx, mode)
      let (t1, xVal, _) = parseE tail
      match t1 with
      | Comma :: t2 ->
          let (t3, _dxVal, _) = parseE t2
          match t3 with
          | Comma :: Ident mode :: Rpar :: rest ->
              // side-effect: compute y at current xVal and record point
              let yVal = evalYAt xVal
              plotPoints <- (xVal, yVal) :: plotPoints
              plotMode <- Some(mode.ToLowerInvariant())
              rest, yVal, (not (isIntLike yVal))
          | _ -> raise (ParseError "Expected: plot(x, dx, mode)")
      | _ -> raise (ParseError "Expected comma in plot arguments")
  | Ident "diff" :: Lpar :: tail ->
      // diff(x0[, h]) derivative of y at x0: try symbolic LUT first; fallback to numeric central difference
      let (t1, x0, _) = parseE tail
      let h, rest =
        match t1 with
        | Rpar :: rest -> 1e-5, rest
        | Comma :: t2 ->
            let (t3, hVal, _) = parseE t2
            match t3 with
            | Rpar :: rest -> hVal, rest
            | _ -> raise (ParseError "Expected closing ')' in diff")
        | _ -> raise (ParseError "Expected ',' or ')' in diff")
      match functionTable.TryFind "y" with
      | Some (param, body) ->
          match parseSExpr param body with
          | Some (sexpr, []) ->
              // Evaluate symbolic derivative at x0
              let dsexpr = sdiff sexpr
              let value = seval x0 dsexpr
              // Mark a small symmetric interval around x0 for GUI shading
              let w = if h > 0.0 then Math.Max(5.0 * h, 0.5) else 0.5
              plotRange <- Some (x0 - w, x0 + w)
              rest, value, true
          | _ ->
              // fallback numeric
              let yph = evalYAt (x0 + h)
              let ymh = evalYAt (x0 - h)
              let d = (yph - ymh) / (2.0 * h)
              // Mark a small symmetric interval around x0 for GUI shading
              let w = if h > 0.0 then Math.Max(5.0 * h, 0.5) else 0.5
              plotRange <- Some (x0 - w, x0 + w)
              rest, d, true
      | None -> raise (EvalError "Undefined function: y(x) not defined")
  | Ident "sdiff" :: Lpar :: tail ->
      // Backward-compatible alias of diff(x0[,h]). Prefer using diff.
      let (t1, x0, _) = parseE tail
      let h, rest =
        match t1 with
        | Rpar :: rest -> 1e-5, rest
        | Comma :: t2 ->
            let (t3, hVal, _) = parseE t2
            match t3 with
            | Rpar :: rest -> hVal, rest
            | _ -> raise (ParseError "Expected closing ')' in sdiff")
        | _ -> raise (ParseError "Expected ',' or ')' in sdiff")
      match functionTable.TryFind "y" with
      | Some (param, body) ->
          match parseSExpr param body with
          | Some (sexpr, []) ->
              let dsexpr = sdiff sexpr
              let value = seval x0 dsexpr
              let w = if h > 0.0 then Math.Max(5.0 * h, 0.5) else 0.5
              plotRange <- Some (x0 - w, x0 + w)
              rest, value, true
          | _ ->
              let yph = evalYAt (x0 + h)
              let ymh = evalYAt (x0 - h)
              let d = (yph - ymh) / (2.0 * h)
              let w = if h > 0.0 then Math.Max(5.0 * h, 0.5) else 0.5
              plotRange <- Some (x0 - w, x0 + w)
              rest, d, true
      | None -> raise (EvalError "Undefined function: y(x) not defined")
  | Ident "integrate" :: Lpar :: tail ->
      // integrate(a, b[, n]) trapezoidal rule on y(x)
      let (t1, a, _) = parseE tail
      match t1 with
      | Comma :: t2 ->
          let (t3, b, _) = parseE t2
          let n, rest =
            match t3 with
            | Rpar :: rest -> 100, rest
            | Comma :: t4 ->
                let (t5, nVal, _) = parseE t4
                match t5 with
                | Rpar :: rest -> max 1 (int (absf nVal)) , rest
                | _ -> raise (ParseError "Expected closing ')' in integrate")
            | _ -> raise (ParseError "Expected ',' or ')' in integrate")
          let aa, bb = if a <= b then a, b else b, a
          let nn = max 1 n
          let h = (bb - aa) / float nn
          let mutable sum = 0.0
          let mutable i = 1
          while i < nn do
            let x = aa + float i * h
            sum <- sum + evalYAt x
            i <- i + 1
          let area = (h/2.0) * (evalYAt aa + 2.0*sum + evalYAt bb)
          plotRange <- Some (aa, bb) // mark range for GUI shading
          rest, area, true
      | _ -> raise (ParseError "Expected comma in integrate arguments")
  | Ident "root_bisect" :: Lpar :: tail ->
      // root_bisect(a, b[, tol][, maxIter])
      let (t1, a, _) = parseE tail
      match t1 with
      | Comma :: t2 ->
          let (t3, b, _) = parseE t2
          let tol, maxIter, rest =
            match t3 with
            | Rpar :: rest -> 1e-6, 100, rest
            | Comma :: t4 ->
                let (t5, tolV, _) = parseE t4
                match t5 with
                | Rpar :: rest -> tolV, 100, rest
                | Comma :: t6 ->
                    let (t7, itV, _) = parseE t6
                    match t7 with
                    | Rpar :: rest -> tolV, max 1 (int (absf itV)), rest
                    | _ -> raise (ParseError "Expected ')' after maxIter")
                | _ -> raise (ParseError "Expected ')' or ',' after tol")
            | _ -> raise (ParseError "Expected ',' or ')' in root_bisect")
          let fa = evalYAt a
          let fb = evalYAt b
          // If an endpoint is exactly a root, nudge it inward so we search for an interior root
          let bumpTowards a b =
            let step = if tol > 0.0 then tol else 1e-12
            if a < b then a + step else a - step
          let mutable aa = a
          let mutable bb = b
          let mutable fa1 = fa
          let mutable fb1 = fb
          if fa1 = 0.0 then
            aa <- bumpTowards a b
            fa1 <- evalYAt aa
          if fb1 = 0.0 then
            bb <- bumpTowards b a
            fb1 <- evalYAt bb
          if fa1 * fb1 > 0.0 then raise (EvalError "Bisection requires f(a)*f(b) <= 0")
          let mutable lo = aa
          let mutable hi = bb
          let mutable flo = fa1
          let mutable fhi = fb1
          let mutable mid = (lo + hi) / 2.0
          let mutable fmid = evalYAt mid
          let mutable iter = 0
          while (absf (hi - lo) > tol) && iter < maxIter do
            mid <- (lo + hi) / 2.0
            fmid <- evalYAt mid
            if flo * fmid <= 0.0 then
              hi <- mid
              fhi <- fmid
            else
              lo <- mid
              flo <- fmid
            iter <- iter + 1
          // normalize very small roots to 0 for stable output
          let rootB = if absf mid < Math.Max(tol, 1e-12) then 0.0 else mid
          rest, rootB, true
      | _ -> raise (ParseError "Expected comma in root_bisect arguments")
  | Ident "root_newton" :: Lpar :: tail ->
      // root_newton(x0[, tol][, maxIter]) using numeric derivative
      let (t1, x0, _) = parseE tail
      let tol, maxIter, rest =
        match t1 with
        | Rpar :: rest -> 1e-6, 50, rest
        | Comma :: t2 ->
            let (t3, tolV, _) = parseE t2
            match t3 with
            | Rpar :: rest -> tolV, 50, rest
            | Comma :: t4 ->
                let (t5, itV, _) = parseE t4
                match t5 with
                | Rpar :: rest -> tolV, max 1 (int (absf itV)), rest
                | _ -> raise (ParseError "Expected ')' after maxIter")
            | _ -> raise (ParseError "Expected ')' or ',' after tol")
        | _ -> raise (ParseError "Expected ',' or ')' in root_newton")
      let mutable x = x0
      let h = 1e-5
      let mutable i = 0
      while i < maxIter do
        let fx = evalYAt x
        if absf fx < tol then i <- maxIter else
        let d = (evalYAt (x + h) - evalYAt (x - h)) / (2.0*h)
        if d = 0.0 then raise (EvalError "Zero derivative in Newton method")
        x <- x - fx / d
        i <- i + 1
      let rootN = if absf x < Math.Max(tol, 1e-12) then 0.0 else x
      rest, rootN, true
  | Ident "root_secant" :: Lpar :: tail ->
      // root_secant(x0, x1[, tol][, maxIter])
      let (t1, x0, _) = parseE tail
      match t1 with
      | Comma :: t2 ->
          let (t3, x1, _) = parseE t2
          let tol, maxIter, rest =
            match t3 with
            | Rpar :: rest -> 1e-6, 50, rest
            | Comma :: t4 ->
                let (t5, tolV, _) = parseE t4
                match t5 with
                | Rpar :: rest -> tolV, 50, rest
                | Comma :: t6 ->
                    let (t7, itV, _) = parseE t6
                    match t7 with
                    | Rpar :: rest -> tolV, max 1 (int (absf itV)), rest
                    | _ -> raise (ParseError "Expected ')' after maxIter")
                | _ -> raise (ParseError "Expected ')' or ',' after tol")
            | _ -> raise (ParseError "Expected ',' or ')' in root_secant")
          let mutable xPrev = x0
          let mutable xCurr = x1
          let mutable fPrev = evalYAt xPrev
          let mutable fCurr = evalYAt xCurr
          let mutable i = 0
          while i < maxIter && absf fCurr > tol do
            let denom = (fCurr - fPrev)
            if denom = 0.0 then raise (EvalError "Zero slope in Secant method")
            let xNext = xCurr - fCurr * (xCurr - xPrev) / denom
            xPrev <- xCurr
            fPrev <- fCurr
            xCurr <- xNext
            fCurr <- evalYAt xCurr
            i <- i + 1
          let rootS = if absf xCurr < Math.Max(tol, 1e-12) then 0.0 else xCurr
          rest, rootS, true
      | _ -> raise (ParseError "Expected comma in root_secant arguments")
  | Ident "tangent" :: Lpar :: tail ->
      // tangent(x0, halfWidth[, mode]) – buffers a short tangent segment for plotting and shades its local interval
      let (t1, x0, _) = parseE tail
      match t1 with
      | Comma :: t2 ->
          let (t3, halfW, _) = parseE t2
          let modeStr, rest =
            match t3 with
            | Rpar :: rest -> "linear", rest
            | Comma :: Ident m :: Rpar :: rest -> m.ToLowerInvariant(), rest
            | _ -> raise (ParseError "Expected ')' or ', mode' in tangent")
          let y0 = evalYAt x0
          let h = 1e-5
          let d = (evalYAt (x0 + h) - evalYAt (x0 - h)) / (2.0*h)
          let xL = x0 - halfW
          let xR = x0 + halfW
          let yL = y0 + d * (xL - x0)
          let yR = y0 + d * (xR - x0)
          plotPoints <- (xL, yL) :: (xR, yR) :: plotPoints
          plotMode <- Some(modeStr)
          // Set plotRange so GUI shades this local interval
          let a = Math.Min(xL, xR)
          let b = Math.Max(xL, xR)
          plotRange <- Some (a, b)
          rest, y0, true
      | _ -> raise (ParseError "Expected comma in tangent arguments")
  | Ident name :: Lpar :: tail ->
      // Try single-argument call first (user-defined or built-in 1-arg)
      let (afterArg, argVal, argFloat) = parseE tail
      match afterArg with
      | Rpar :: rest ->
          match functionTable.TryFind name with
          | Some (param, body) ->
              let oldBinding = symbolTable.TryFind param
              symbolTable <- symbolTable.Add(param, argVal)
              let (afterBody, result, resFloat) = parseE body
              if afterBody <> [] then
                  match oldBinding with
                  | Some v -> symbolTable <- symbolTable.Add(param, v)
                  | None -> symbolTable <- symbolTable.Remove param
                  raise (ParseError $"Extra tokens in function body of {name}")
              match oldBinding with
              | Some v -> symbolTable <- symbolTable.Add(param, v)
              | None -> symbolTable <- symbolTable.Remove param
              rest, result, (argFloat || resFloat)
          | None ->
              // Built-ins (unary and var-args)
              let low = name.ToLowerInvariant()
              let applyUnary (f: float -> float) =
                  let r = f argVal
                  rest, r, true
              match low with
              | "sin" -> applyUnary Math.Sin
              | "cos" -> applyUnary Math.Cos
              | "tan" -> applyUnary Math.Tan
              | "exp" -> applyUnary Math.Exp
              | "log" -> if argVal <= 0.0 then raise (EvalError "log domain error") else applyUnary Math.Log
              | "sqrt" -> if argVal < 0.0 then raise (EvalError "sqrt domain error") else applyUnary Math.Sqrt
              | "abs" -> applyUnary absf
              | _ -> raise (EvalError $"Undefined function or wrong arity: {name}")
  | Ident name :: tail ->
      match symbolTable.TryFind name with
      | Some v -> tail, v, (not (isIntLike v))
      | None -> raise (EvalError $"Undefined variable: {name}")
  | Lpar :: tail ->
      let (t2, v, f) = parseE tail
      match t2 with
      | Rpar :: rest -> rest, v, f
      | _ -> raise (ParseError "Missing closing parenthesis")
  | _ -> raise (ParseError "Unexpected token")

// Evaluation
let parseAndEval tokens =
  let rec parseStatement toks =
    match toks with
    // function definition: f(x) = <expr>
    | Ident fname :: Lpar :: Ident param :: Rpar :: Assign :: rest ->
        functionTable <- functionTable.Add(fname, (param, rest))
        [], 0.0, false
    // for-loop: for x = a to b step s do <expr>
    | Ident "for" :: Ident var :: Assign :: rest ->
        let (tA, a, _) = parseE rest
        match tA with
        | Ident "to" :: tB ->
            let (tS, b, _) = parseE tB
            let (tBodyStart, stepVal) =
              match tS with
              | Ident "step" :: tStep ->
                  let (tBody, s, _) = parseE tStep
                  tBody, s
              | _ -> tS, 1.0
            match tBodyStart with
            | Ident "do" :: body ->
                if stepVal = 0.0 then raise (EvalError "Step must be non-zero")
                // Do not set plotRange here; shading should only be set by integrate/shade
                let compare = if stepVal > 0.0 then (fun x -> x <= b + 1e-12) else (fun x -> x >= b - 1e-12)
                let mutable x = a
                let mutable lastRes = 0.0
                let oldBinding = symbolTable.TryFind var
                try
                    while compare x do
                        symbolTable <- symbolTable.Add(var, x)
                        let (restAfter, v, f) = parseE body
                        if restAfter <> [] then raise (ParseError "Extra tokens after for body expression")
                        lastRes <- v
                        x <- x + stepVal
                    // restore
                    match oldBinding with
                    | Some vOld -> symbolTable <- symbolTable.Add(var, vOld)
                    | None -> symbolTable <- symbolTable.Remove var
                    [], lastRes, not (isIntLike lastRes)
                with ex ->
                    // restore then rethrow
                    match oldBinding with
                    | Some vOld -> symbolTable <- symbolTable.Add(var, vOld)
                    | None -> symbolTable <- symbolTable.Remove var
                    raise ex
            | _ -> raise (ParseError "Expected 'do' in for-loop")
        | _ -> raise (ParseError "Expected 'to' in for-loop")
    // assignment: x = <expr>
    | Ident name :: Assign :: rest ->
        let (afterExpr, value, _) = parseE rest
        symbolTable <- symbolTable.Add(name, value)
        afterExpr, value, false
    // expression
    | _ -> parseE toks
  let (rest, value, isFloat) = parseStatement tokens
  if rest <> [] then raise (ParseError "Extra tokens after expression")
  value, isFloat

let EvaluateExpression (input: string) =
    try
        if obj.ReferenceEquals(symbolTable, null) then
            symbolTable <- Map.empty
        if obj.ReferenceEquals(functionTable, null) then
            functionTable <- Map.empty
        // Reset shading at the start of each evaluation batch so only this batch's
        // integrate/shade calls can enable area fill on the GUI.
        plotRange <- None

        let lines = 
            input.Split([|'\n'; ';'|], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s <> "")

        let mutable lastResult = 0.0
        let mutable isFloat = false

        for line in lines do
            let tokens = lexer line
            let (result, f) = parseAndEval tokens
            lastResult <- result
            isFloat <- f

        // Normalize tiny values to +0.0 to avoid "-0" in output
        if absf lastResult < 5e-13 then lastResult <- 0.0

        if isFloat then lastResult.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)
        else lastResult.ToString("0", System.Globalization.CultureInfo.InvariantCulture)

    with
    | LexError msg -> $"Lexer error: {msg}"
    | ParseError msg -> $"Parser error: {msg}"
    | EvalError msg -> $"Runtime error: {msg}"
    | ex -> $"Error: {ex.Message}"

let ResetState () =
    symbolTable <- Map.empty
    functionTable <- Map.empty
    plotPoints <- []
    plotMode <- None
    plotRange <- None

let HasPlotData () = plotPoints <> []
let GetPlotData () = plotPoints |> List.rev |> List.toArray
let GetPlotMode () = match plotMode with | Some m -> m | None -> "linear"
let GetPlotRange () = match plotRange with | Some (a,b) -> (a,b) | None -> (0.0,1.0)
let HasShadingRange () = match plotRange with | Some _ -> true | None -> false
let ClearPlotData () = plotPoints <- []; plotMode <- None; plotRange <- None

// -------- Transpiler to C# (INT5) --------

let private genModuloExpr (a:string) (b:string) =
    // emulate intMod semantics on doubles: a - b * Truncate(a/b)
    $"({a} - {b} * System.Math.Truncate({a} / {b}))"

let rec private genPrimary (tokens: Token list) : (string * Token list) =
    match tokens with
    | Number (n, hadDot) :: tail ->
        let s = n.ToString(System.Globalization.CultureInfo.InvariantCulture)
        (s, tail)
    | Ident name :: Lpar :: tail ->
        // one-argument function call
        let (argStr, rest1) =
            let (s, rest) = genExpr tail in
            match rest with
            | Rpar :: rest2 -> s, rest2
            | _ -> raise (ParseError $"Missing ')' after argument of {name}")
        let low = name.ToLowerInvariant()
        let call =
            match low with
            | "sin" -> $"System.Math.Sin({argStr})"
            | "cos" -> $"System.Math.Cos({argStr})"
            | "tan" -> $"System.Math.Tan({argStr})"
            | "exp" -> $"System.Math.Exp({argStr})"
            | "log" -> $"System.Math.Log({argStr})"
            | "sqrt" -> $"System.Math.Sqrt({argStr})"
            | "abs" -> $"System.Math.Abs({argStr})"
            | _ -> $"{name}({argStr})" // user-defined function
        call, rest1
    | Ident name :: tail ->
        // variable (e.g., parameter)
        name, tail
    | Lpar :: tail ->
        let (s, rest) = genExpr tail
        match rest with
        | Rpar :: rest2 -> $"({s})", rest2
        | _ -> raise (ParseError "Missing closing parenthesis")
    | _ -> raise (ParseError "Unexpected token in primary for codegen")

and private genUnary tokens : (string * Token list) =
    match tokens with
    | Minus :: tail ->
        let (s, rest) = genUnary tail
        $"(-{s})", rest
    | Plus :: tail -> genUnary tail
    | _ -> genPrimary tokens

and private genPower tokens : (string * Token list) =
    // left-assoc '^' → nest Math.Pow accordingly
    let (uStr, t1) = genUnary tokens
    let rec loop acc toks =
        match toks with
        | Pow :: tail ->
            let (rhs, rest) = genUnary tail
            let accStr = $"System.Math.Pow({acc}, {rhs})"
            loop accStr rest
        | _ -> acc, toks
    loop uStr t1

and private genTerm tokens : (string * Token list) =
    let (fStr, t1) = genPower tokens
    let rec loop acc toks =
        match toks with
        | Mul :: tail ->
            let (rhs, rest) = genPower tail
            loop ($"({acc} * {rhs})") rest
        | Div :: tail ->
            let (rhs, rest) = genPower tail
            loop ($"({acc} / {rhs})") rest
        | Mod :: tail ->
            let (rhs, rest) = genPower tail
            loop (genModuloExpr acc rhs) rest
        | _ -> acc, toks
    loop fStr t1

and private genExpr tokens : (string * Token list) =
    let (tStr, t1) = genTerm tokens
    let rec loop acc toks =
        match toks with
        | Plus :: tail ->
            let (rhs, rest) = genTerm tail
            loop ($"({acc} + {rhs})") rest
        | Minus :: tail ->
            let (rhs, rest) = genTerm tail
            loop ($"({acc} - {rhs})") rest
        | _ -> acc, toks
    loop tStr t1

let private tryParseFuncDef (tokens: Token list) : (string * string * Token list) option =
    match tokens with
    | Ident fname :: Lpar :: Ident param :: Rpar :: Assign :: rest -> Some (fname, param, rest)
    | _ -> None

let private generateMethod (name:string) (param:string) (bodyTokens: Token list) : string =
    let (bodyExpr, rest) = genExpr bodyTokens
    if rest <> [] then raise (ParseError $"Extra tokens after body of {name}")
    // sanitize parameter and references: assume 'x' for main; leave as-is
    $"    public static double {name}(double {param}) => {bodyExpr};"

let TranspileToCSharp (source: string) : string =
    try
        // collect function defs
        let lines = 
            source.Split([|'\n'; ';'|], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s <> "")
        if lines.Length = 0 then raise (ParseError "Empty source")
        let mutable defs : (string * string * Token list) list = []
        for line in lines do
            let toks = lexer line
            match tryParseFuncDef toks with
            | Some def -> defs <- def :: defs
            | None -> () // ignore other statements for compiler
        let defs = defs |> List.rev
        // choose entry function: y if present, else the first defined function
        let entryNameOpt =
            defs |> List.tryFind (fun (n,_,_) -> String.Equals(n, "y", StringComparison.OrdinalIgnoreCase))
            |> Option.orElse (defs |> List.tryHead)
        let entryName =
            match entryNameOpt with
            | Some (n,_,_) -> n
            | None -> raise (EvalError "Transpiler requires at least one function definition, e.g., f(x)=x^2+1")
        // generate methods
        let sb = StringBuilder()
        sb.AppendLine("using System;") |> ignore
        sb.AppendLine("using System.Globalization;") |> ignore
        sb.AppendLine("namespace GeneratedMath") |> ignore
        sb.AppendLine("{") |> ignore
        sb.AppendLine("  public static class UserFuncs") |> ignore
        sb.AppendLine("  {") |> ignore
        for (n,p,b) in defs do
            let methodLine = generateMethod n p b
            sb.AppendLine(methodLine) |> ignore
        sb.AppendLine("    public static int Main(string[] args)") |> ignore
        sb.AppendLine("    {") |> ignore
        sb.AppendLine("      try {") |> ignore
        sb.AppendLine("        if (args.Length == 1)") |> ignore
        sb.AppendLine("        {") |> ignore
        sb.AppendLine("          var x = double.Parse(args[0], CultureInfo.InvariantCulture);") |> ignore
        sb.AppendLine($"          var yv = {entryName}(x);") |> ignore
        sb.AppendLine("          Console.WriteLine(yv.ToString(\"G17\", CultureInfo.InvariantCulture));") |> ignore
        sb.AppendLine("          return 0;") |> ignore
        sb.AppendLine("        }") |> ignore
        sb.AppendLine($"        Console.WriteLine(\"Usage: <exe> <x>    // evaluates {entryName}(x)\");") |> ignore
        sb.AppendLine("        return 1;") |> ignore
        sb.AppendLine("      } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }") |> ignore
        sb.AppendLine("    }") |> ignore
        sb.AppendLine("  }") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString()
    with
    | LexError msg -> $"CSharp error: Lexer error: {msg}"
    | ParseError msg -> $"CSharp error: Parser error: {msg}"
    | EvalError msg -> $"CSharp error: Runtime error: {msg}"
    | ex -> $"CSharp error: {ex.Message}"

let EvaluateExprForX (expr: string) (x: float) =
    try
        if obj.ReferenceEquals(symbolTable, null) then
            symbolTable <- Map.empty
        if obj.ReferenceEquals(functionTable, null) then
            functionTable <- Map.empty
        // Nudge x slightly to avoid integer-like artifacts in plotting (e.g., x/3 at integer x)
        // isClose uses ~1e-10, so pick eps significantly larger to reliably break ties
        let eps = Math.Max(1e-8, Math.Abs(x) * 1e-8)
        let xAdj = x + eps
        let oldX = symbolTable.TryFind "x"
        symbolTable <- symbolTable.Add("x", xAdj)
        // Force float context during plotting by wrapping expression in 1.0*( ... )
        let exprFloat = "(1.0*(" + expr + "))"
        let tokens = lexer exprFloat
        let (value, _) = parseAndEval tokens
        // Restore previous x binding
        match oldX with
        | Some v -> symbolTable <- symbolTable.Add("x", v)
        | None -> symbolTable <- symbolTable.Remove "x"
        value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    with
    | LexError msg -> $"Lexer error: {msg}"
    | ParseError msg -> $"Parser error: {msg}"
    | EvalError msg -> $"Runtime error: {msg}"
    | ex -> $"Error: {ex.Message}"
