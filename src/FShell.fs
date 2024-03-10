[<AutoOpen>]
module FShell

open System
open System.IO
open System.Diagnostics

type Args = 
  | Recurse
  | Force
  | Depth of int
  | Directory
  | File

let _recurse = Recurse
let _force = Force
let _depth i = Depth i
let _directory = Directory
let _file = File

type Cmd =

  static member pwd = Directory.GetCurrentDirectory()

  static member ls' (args:Args seq) = fun path -> 
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
          let newArgs = args |> Seq.filter (function Depth _ -> false | _ -> true) |> Seq.append [Depth (n-1)]
          yield! Cmd.ls' newArgs subDir
      |]
    | true, None -> 
      [|
        yield! thisDir 
        for subDir in Directory.GetDirectories path do
          yield! Cmd.ls' args subDir
      |]

  static member ls' (path:string) = Cmd.ls' [] path

  static member ls = Cmd.ls' "."

  static member cd path = Directory.SetCurrentDirectory path
  static member cat path = File.ReadAllLines path
  static member cp (args:Args seq) = fun src dst ->
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
          Cmd.cp args subDir.FullName newDestinationDir

    else failwith "Path does not exist"

  static member cp (src:string) = fun dst -> Cmd.cp [] src dst

  static member rm (args:Args seq) = fun (path:string) -> 
    if File.Exists path then File.Delete path
    else if Directory.Exists path then 
      let recurse = Seq.contains Recurse args
      Directory.Delete(path, recurse)
    else failwith "Path does not exist"


  static member rm (path:string) = Cmd.rm [] path


  static member mv src dst = File.Move(src, dst)
  static member mkdir path = Directory.CreateDirectory path
  static member md path = Directory.CreateDirectory path
  static member touch path = File.Create(path) |> ignore
  static member exec (cmd:string, [<ParamArray>] args) = 
    let args = String.Join(" ", args |> Array.map (sprintf "\"%s\""))
    let proc = new Process(StartInfo = ProcessStartInfo(FileName = cmd, Arguments = args))
    proc.Start() |> ignore
    proc.WaitForExit()

  static member exec'' (cmd:string, [<ParamArray>] args) = fun (xs:string array) ->
    let args = String.Join(" ", args |> Array.map (sprintf "\"%s\""))
    let config = ProcessStartInfo(FileName = cmd, Arguments = args, RedirectStandardInput = true)
    let proc = new Process(StartInfo = config)
    proc.Start() |> ignore
    for x in xs do
      proc.StandardInput.WriteLine(x)
    proc.StandardInput.Close()
    proc.WaitForExit()

  static member exec' (cmd:string, [<ParamArray>] args) = fun (x:string) -> Cmd.exec'' (cmd, args) [|x|]

  static member start (path:string) = Process.Start("explorer", path).WaitForExit()

  static member which cmd = failwith "Not implemented"
  static member grep pattern = failwith "Not implemented"
  static member echo msg = printfn "%s" msg
  static member write (txt:string) = fun (path:string) -> File.WriteAllText(path, txt)
  static member write (lines:string seq) = fun (path:string) -> File.WriteAllLines(path, lines)
  static member append (txt:string) = fun (path:string) -> File.AppendAllText(path, txt)
  static member append (lines:string seq) = fun (path:string) -> File.AppendAllLines(path, lines)

#if INTERACTIVE
fsi.AddPrinter(fun (x:string seq) -> Environment.NewLine + String.Join(Environment.NewLine, x))
#endif

open type Cmd
