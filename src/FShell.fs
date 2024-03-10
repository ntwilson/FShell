#if COMPILED
[<AutoOpen>]
module FShell
#else
#r "nuget: Microsoft.Extensions.FileSystemGlobbing"
#endif

open System
open System.IO
open System.Diagnostics
open Microsoft.Extensions.FileSystemGlobbing

type Args = 
  | Args of string seq
  | Depth of int
  | Directory
  | Exclude of string
  | File
  | Force
  | NoCapture
  | Path of string
  | Pattern of string
  | Recurse
  | Silent

type Help = Help

let _args xs = Args xs
let _depth i = Depth i
let _directory = Directory
let _exclude s = Exclude s
let _file = File
let _force = Force
let _help = Help
let _noCapture = NoCapture
let _path s = Path s
let _pattern s = Pattern s
let _recurse = Recurse
let _silent = Silent

/// pipe forward into a mapping function. Similar to the `| %` operator in PowerShell
let (|%) xs f = Seq.toList xs |> List.map f
/// pipe forward into a filtering function. Similar to the `| ?` operator in PowerShell
let (|?) xs f = Seq.filter f xs

type FShell =

  static member private lsWithMatcher args =
    let path = args |> Seq.tryPick (function Path p -> Some p | _ -> None) |> Option.defaultValue "."
    let includes = args |> Seq.choose (function Pattern p -> Some p | _ -> None)
    let excludes = args |> Seq.choose (function Exclude p -> Some p | _ -> None)

    let matcher = 
      let m = Matcher()
      m.AddIncludePatterns includes
      m.AddExcludePatterns excludes
      m

    matcher.GetResultsInFullPath(path) |> Array.ofSeq



  static member pwd = Directory.GetCurrentDirectory()

  static member ls' Help = 
    printfn """
List contents of a directory.
`ls` by itself will list the current directory contents. Use `ls'` for any other usage. 
`ls' "path"` will list the contents at "path". 
`ls' "*.fs"` will do a pattern search in the current directory. 

Arguments may be used instead of a string pattern or path, e.g., `ls' (_file, _recurse, _path "path")`. 
If using a pattern (either with a string pattern, or the `_pattern` or `_exclude` flags), then the flags 
`_recurse`, `_depth`, `_file`, and `_directory` will be ignored, and it will use File Globbing from the 
Microsoft.Extensions.FileSystemGlobbing package. Note that you can recurse using the pattern, e.g., `ls' "**/*.fsproj`.

Available arguments:
- `_recurse` - Recurse into subdirectories. This will be overridden by a pattern or exclude pattern.
- `_depth n` - Recurse into subdirectories up to `n` levels deep. This will be overridden by a pattern or exclude pattern.
- `_directory` - Only list directories. This will be overridden by a pattern or exclude pattern.
- `_file` - Only list files. This will be overridden by a pattern or exclude pattern.
- `_path "path"` - List contents of the specified path
- `_pattern "pattern"` - List contents matching the specified pattern. This can be specified multiple times.
- `_exclude "pattern"` - Exclude contents matching the specified pattern. This can be specified multiple times."""

  /// List contents of a directory.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories. This will be overridden by a pattern or exclude pattern.
  /// - `_depth n` - Recurse into subdirectories up to `n` levels deep. This will be overridden by a pattern or exclude pattern.
  /// - `_directory` - Only list directories. This will be overridden by a pattern or exclude pattern.
  /// - `_file` - Only list files. This will be overridden by a pattern or exclude pattern.
  /// - `_path "path"` - List contents of the specified path
  /// - `_pattern "pattern"` - List contents matching the specified pattern. This can be specified multiple times.
  /// - `_exclude "pattern"` - Exclude contents matching the specified pattern. This can be specified multiple times.
  static member ls' ([<ParamArray>] args:Args array) = 
    let path = args |> Array.tryPick (function Path p -> Some p | _ -> None) |> Option.defaultValue "."
    if Array.exists (function Pattern _ -> true | Exclude _ -> true | _ -> false) args
    then FShell.lsWithMatcher args
    else
      let recurse = Seq.contains Recurse args
      let subDirs = Directory.GetDirectories path
      let thisDir = 
        if Seq.contains Directory args then subDirs
        else if Seq.contains File args then Directory.GetFiles path
        else [| yield! subDirs; yield! Directory.GetFiles path |]

      match recurse, Seq.tryPick (function Depth i -> Some i | _ -> None) args with
      | false, _
      | true, Some 0 -> thisDir
      | true, Some n ->
        [|
          yield! thisDir 
          for subDir in subDirs do
            let newArgs = 
              [|
                yield! args |> Seq.filter (function Depth _ -> false | Path _ -> false | _ -> true) 
                Depth (n-1)
                Path subDir
              |]
            yield! FShell.ls' newArgs
        |]
      | true, None -> 
        [|
          yield! thisDir 
          for subDir in Directory.GetDirectories path do
            let newArgs = 
              [|
                yield! args |> Seq.filter (function Depth _ -> false | Path _ -> false | _ -> true) 
                Path subDir
              |]
            yield! FShell.ls' newArgs
        |]

  static member ls' (str:string) = 
    if Directory.Exists str then FShell.ls' (Path str)
    else FShell.ls' (Pattern str)

  static member ls = FShell.ls' ()

  static member cd path = Directory.SetCurrentDirectory path
  static member cat path = File.ReadAllLines path

  /// Copy files or directories.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories
  static member cp Help =
    printfn """
Copy files or directories.
Available arguments:
- `_recurse` - Recurse into subdirectories"""

  /// Copy files or directories.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories
  static member cp ([<ParamArray>] args:Args array) = fun src dst ->
    if File.Exists src then File.Copy(src, dst)
    else if Directory.Exists src then 
      let recurse = Seq.contains Recurse args
      let dir = DirectoryInfo(src)

      if not dir.Exists
      then raise (DirectoryNotFoundException $"Source directory not found: %s{dir.FullName}")

      let dirs = dir.GetDirectories()
      Directory.CreateDirectory(dst) |> ignore

      for file in dir.GetFiles() do
        let targetFilePath = Path.Combine(dst, file.Name)
        file.CopyTo(targetFilePath) |> ignore

      if recurse
      then
        for subDir in dirs do
          let newDestinationDir = Path.Combine(dst, subDir.Name)
          FShell.cp args subDir.FullName newDestinationDir

    else failwith "Path does not exist"

  /// Copy files or directories.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories
  static member cp (src:string) = fun dst -> FShell.cp () src dst

  /// Remove files or directories.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories
  static member rm Help =
    printfn """
Remove files or directories.
Available arguments:
- `_recurse` - Recurse into subdirectories"""

  /// Remove files or directories.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories
  static member rm ([<ParamArray>] args:Args array) = fun (path:string) -> 
    if File.Exists path then File.Delete path
    else if Directory.Exists path then 
      let recurse = Seq.contains Recurse args
      Directory.Delete(path, recurse)
    else failwith "Path does not exist"


  /// Remove files or directories.
  /// Available arguments:
  /// - `_recurse` - Recurse into subdirectories
  static member rm (path:string) = FShell.rm () path


  static member mv src dst = File.Move(src, dst)
  static member mkdir path = Directory.CreateDirectory path
  static member md path = Directory.CreateDirectory path
  static member touch path = File.Create(path) |> ignore

  /// Execute a command and pass an array of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> execArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execArr Help =
    printfn """
Execute any system command. Use `exec`/`cmd` if you have nothing to pipe into stdin, 
and use `execArr`/`execLst`/`execStr`/`cmdArr`/`cmdLst`/`cmdStr` to pipe some data into the command via stdin.

By default this will capture the output of the command and both return it in an array and print it to the console in real time.
You can specify arguments to the command in the command text (e.g., `exec "dotnet build"`), 
or you can pass them as additional string parameters  (e.g., `exec ("dotnet", "build", "--help")`), 
or you can pass them as additional Args parameters (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).

You can pass in string data via stdin, e.g., `cat "file.txt" |> execArr "clip"`.

Available arguments:
- `_silent` - Do not print the output of the command to the console in real time
- `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
- `_args` - A sequence of string arguments to pass to the command"""

  /// Execute a command and pass an array of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> execArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execArr (cmd:string, [<ParamArray>] args : Args array) = fun (xs:string array) ->
    let cmdArgs = 
      args |> Array.tryPick (function 
        | Args xs -> Some (String.Join(" ", xs |> Seq.map (sprintf "\"%s\""))) 
        | _ -> None
      ) 
      |> Option.defaultValue ""

    let (cmd, cmdArgs) =
      if (cmd.Contains(" ") && cmdArgs = "") 
      then (cmd.Split(' ').[0], String.Join(" ", cmd.Split(' ').[1..]))
      else (cmd, cmdArgs)

    let redirectIn = not (Array.isEmpty xs)
    let redirectOut = not (Seq.contains NoCapture args)
    let config = ProcessStartInfo(FileName=cmd, Arguments=cmdArgs, RedirectStandardOutput=redirectOut, RedirectStandardInput=redirectIn)
    let proc = new Process(StartInfo=config)

    let mutable output = ResizeArray []
    let silent = Seq.contains Silent args
    if redirectOut then
      proc.OutputDataReceived.Add(fun (e:DataReceivedEventArgs) -> 
        output.Add(e.Data)
        if not silent then printfn "%s" e.Data
      )

    proc.Start() |> ignore
    if redirectOut then proc.BeginOutputReadLine()
    if redirectIn then
      for x in xs do
        proc.StandardInput.WriteLine(x)
      proc.StandardInput.Close()
    proc.WaitForExit()

    Array.ofSeq output

  /// Execute a command and pass an array of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> execArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execArr (cmd:string, [<ParamArray>] args : string array) = FShell.execArr (cmd, Args args)

  /// Execute a command and pass an array of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> execArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execArr ([<ParamArray>] args : Args array) = fun (cmd:string) -> FShell.execArr (cmd, args)

  /// Execute a command and pass an array of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> execArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execArr (cmd:string) = FShell.execArr (cmd, ([||]:Args array))

  /// Execute a command. Use `execArr` or `execLst` or `execStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member exec Help = FShell.execArr Help

  /// Execute a command. Use `execArr` or `execLst` or `execStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member exec (cmd:string, [<ParamArray>] args : Args array) = FShell.execArr (cmd, args) [||]

  /// Execute a command. Use `execArr` or `execLst` or `execStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member exec (cmd:string, [<ParamArray>] args : string array) = FShell.exec (cmd, Args args)

  /// Execute a command. Use `execArr` or `execLst` or `execStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member exec ([<ParamArray>] args : Args array) = fun (cmd:string) -> FShell.execArr (cmd, args) [||]

  /// Execute a command. Use `execArr` or `execLst` or `execStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member exec (cmd:string) = FShell.exec (cmd, ([||]:Args array))

  /// Execute a command and pass a list of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> execLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execLst Help = FShell.execArr Help

  /// Execute a command and pass a list of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> execLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execLst (cmd:string, [<ParamArray>] args : Args array) = fun (xs:string list) -> FShell.execArr (cmd, args) (Array.ofList xs)

  /// Execute a command and pass a list of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> execLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execLst (cmd:string, [<ParamArray>] args : string array) = fun (xs:string list) -> FShell.execArr (cmd, args) (Array.ofList xs)

  /// Execute a command and pass a list of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> execLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execLst ([<ParamArray>] args : Args array) = fun (cmd:string) -> fun (xs:string list) -> FShell.execArr (cmd, args) (Array.ofList xs)

  /// Execute a command and pass a list of strings into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> execLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execLst (cmd:string) = fun (xs:string list) -> FShell.execArr (cmd, ([||]:Args array)) (Array.ofList xs)

  /// Execute a command and pass a string into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> execStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execStr Help = FShell.execArr Help

  /// Execute a command and pass a string into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> execStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execStr (cmd:string, [<ParamArray>] args : Args array) = fun (x:string) -> FShell.execArr (cmd, args) [|x|]

  /// Execute a command and pass a string into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> execStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execStr (cmd:string, [<ParamArray>] args : string array) = fun (x:string) -> FShell.execArr (cmd, args) [|x|]

  /// Execute a command and pass a string into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> execStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execStr ([<ParamArray>] args : Args array) = fun (cmd:string) -> fun (x:string) -> FShell.execArr (cmd, args) [|x|]

  /// Execute a command and pass a string into its stdin. Use `exec` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> execStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member execStr (cmd:string) = fun (x:string) -> FShell.execArr (cmd, ([||]:Args array)) [|x|]


  /// An alias for `exec`. Execute a command. Use `cmdArr` or `cmdLst` or `cmdStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmd Help = FShell.exec Help

  /// An alias for `exec`. Execute a command. Use `cmdArr` or `cmdLst` or `cmdStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmd (cmd:string, [<ParamArray>] args : Args array) = FShell.exec (cmd, args)

  /// An alias for `exec`. Execute a command. Use `cmdArr` or `cmdLst` or `cmdStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmd (cmd:string, [<ParamArray>] args : string array) = FShell.exec (cmd, args)

  /// An alias for `exec`. Execute a command. Use `cmdArr` or `cmdLst` or `cmdStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmd ([<ParamArray>] args : Args array) = fun (cmd:string) -> FShell.exec (cmd, args)

  /// An alias for `exec`. Execute a command. Use `cmdArr` or `cmdLst` or `cmdStr` if you need to pass additional data to stdin.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmd (cmd:string) = FShell.exec (cmd)

  /// An alias for `execArr`. Execute a command and pass an array of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> cmdArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdArr Help = FShell.execArr Help

  /// An alias for `execArr`. Execute a command and pass an array of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> cmdArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdArr (cmd:string, [<ParamArray>] args : Args array) = FShell.execArr (cmd, args)

  /// An alias for `execArr`. Execute a command and pass an array of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> cmdArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdArr (cmd:string, [<ParamArray>] args : string array) = FShell.execArr (cmd, args)

  /// An alias for `execArr`. Execute a command and pass an array of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> cmdArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdArr ([<ParamArray>] args : Args array) = fun (cmd:string) -> FShell.execArr (cmd, args)

  /// An alias for `execArr`. Execute a command and pass an array of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `cat "file.txt" |> cmdArr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdArr (cmd:string) = FShell.execArr (cmd)

  /// An alias for `execLst`. Execute a command and pass a list of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> cmdLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdLst Help = FShell.execLst Help

  /// An alias for `execLst`. Execute a command and pass a list of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> cmdLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdLst (cmd:string, [<ParamArray>] args : Args array) = FShell.execLst (cmd, args)

  /// An alias for `execLst`. Execute a command and pass a list of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> cmdLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdLst (cmd:string, [<ParamArray>] args : string array) = FShell.execLst (cmd, args)

  /// An alias for `execLst`. Execute a command and pass a list of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> cmdLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdLst ([<ParamArray>] args : Args array) = fun (cmd:string) -> FShell.execLst (cmd, args)

  /// An alias for `execLst`. Execute a command and pass a list of strings into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `["line1"; "line2"] |> cmdLst "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdLst (cmd:string) = FShell.execLst (cmd)

  /// An alias for `execStr`. Execute a command and pass a string into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> cmdStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdStr Help = FShell.execStr Help

  /// An alias for `execStr`. Execute a command and pass a string into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> cmdStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdStr (cmd:string, [<ParamArray>] args : Args array) = FShell.execStr (cmd, args)

  /// An alias for `execStr`. Execute a command and pass a string into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> cmdStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdStr (cmd:string, [<ParamArray>] args : string array) = FShell.execStr (cmd, args)

  /// An alias for `execStr`. Execute a command and pass a string into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> cmdStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdStr ([<ParamArray>] args : Args array) = fun (cmd:string) -> FShell.execStr (cmd, args)

  /// An alias for `execStr`. Execute a command and pass a string into its stdin. Use `cmd` if you have nothing to pass into stdin. 
  /// This can be useful to use F# piping `|>` where a shell pipe would be used, e.g, `"push to clipboard" |> cmdStr "clip"`.
  /// By default this will capture the output of the command and both return it in an array and print it to the console in real time.
  /// You can specify arguments in the command text (e.g., `cmd "dotnet build"`), or you can pass them as additional string parameters  
  /// (e.g., `cmd ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters 
  /// (e.g., `cmd ("dotnet", _args ["build"; "--help"], _noCapture)`).
  /// Available arguments:
  /// - `_silent` - Do not print the output of the command to the console in real time
  /// - `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
  /// - `_args` - A sequence of string arguments to pass to the command
  static member cmdStr (cmd:string) = FShell.execStr (cmd)

  /// Windows only. Launch a file using the Operating System's default program for that file type.
  static member start (path:string) = Process.Start("explorer", path).WaitForExit()

  // static member which cmd = failwith "Not implemented"
  // static member grep pattern = failwith "Not implemented"

  /// Write a string to a file. If the file already exists, it will be overwritten.
  static member write (txt:string) = fun (path:string) -> File.WriteAllText(path, txt)
  /// Write a sequence of strings as lines to a file. If the file already exists, it will be overwritten.
  static member write (lines:string seq) = fun (path:string) -> File.WriteAllLines(path, lines)
  /// Append a string to a file. If the file does not exist, it will be created.
  static member append (txt:string) = fun (path:string) -> File.AppendAllText(path, txt)
  /// Append a sequence of strings as lines to a file. If the file does not exist, it will be created.
  static member append (lines:string seq) = fun (path:string) -> File.AppendAllLines(path, lines)

let fshPrinter = fun (x:string seq) -> Environment.NewLine + String.Join(Environment.NewLine, x)

open type FShell
