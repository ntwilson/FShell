# FShell
Turn F# FSI into a shell.

Run in an FSI window or a script file by adding at the top:
```F#
#r "nuget: FShell"
open type FShell
fsi.AddPrinter fshPrinter;;
```

This package adds convenience functions for FSI to make typical shell operations easier. The commands available are

- `ls`/`ls'`
- `pwd`
- `cd`
- `cat`
- `cp`
- `rm`
- `mv`
- `mkdir`/`md`
- `touch`
- `start` (Windows only)
- `write`
- `append`
- `exec`/`execArr`/`execLst`/`execStr`/`cmd`/`cmdArr`/`cmdLst`/`cmdStr`

In addition, you may enable any sequence of strings printing in FSI with one string per line, so that operations like `ls` or `cat` that return an array of strings will display equivalently to many other shell environments. This is achieved with the `fsi.AddPrinter fshPrinter` command.

This package also provides a `|%` and `|?` operator that behave similarly to the corresponding operators in PowerShell.

Arguments to commands always come first, and are prefixed with `_` (reminicent of how arguments in PowerShell are prefixed with `-`, plus you can get tab completion of any arguments). A single argument can be used on its own, e.g., `ls' _recurse`, while multiple arguments must be inside a tuple, e.g., `ls' (_recurse, _file)`. 

## `ls`

`ls` by itself will list the current directory contents. Use `ls'` for any other usage. 

`ls' "path"` will list the contents at "path". 

`ls' "*.fs"` will do a pattern search in the current directory. 

Arguments may be used instead of a string pattern or path, e.g., `ls' (_file, _recurse, _path "path")`. Allowed  arguments include `_recurse`, `_depth`, `_file` (only include files in the result), `_directory` (only include directories in the result), `_path`, `_pattern`, and `_exclude`.

If using a pattern (either with a string pattern, or the `_pattern` or `_exclude` flags), then the flags `_recurse`, `_depth`, `_file`, and `_directory` will be ignored, and it will use File Globbing from the Microsoft.Extensions.FileSystemGlobbing package. Note that you can recurse using the pattern, e.g., `ls' "**/*.fsproj`.

## `pwd`

Just type `pwd` to print the current working directory

## `cd`

Change the current working directory via a string, e.g., `cd "bin/Release/net8.0"`

## `cat`

Get the text contents of a file as an array of strings.

## `cp`

Copy a file or directory. Use without arguments, e.g., `cp "src.txt" "dest.txt"` or with arguments, e.g., `cp _recurse "src" "dest"`. Allowed arguments include `_recurse` 

## `rm`

Remove files or directories. Use without arguments, e.g., `rm "test.txt"` or with arguments, e.g., `rm _recurse "test"`. Allowed arguments include `_recurse`

## `mv`

Move files or directories, e.g., `mv "src.txt" "dest.txt"`

## `mkdir`/`md`

Create a new empty directory, e.g., `mkdir "test"`

## `touch`

Create a new empty file, e.g., `touch "test.txt"`

## `start` (Windows only)

Launch a file using the Operating System's default application for that file type. E.g., `start "report.pdf"`

## `write`

Write a string or a sequence of strings to a file, overwriting it if it exists. E.g., `"hello world" |> write "test.txt"`

## `append`

Append a string or sequence of strings to the end of a file, creating it if it doesn't exist. E.g., `"hello world" |> append "test.txt"`

## `exec`/`execArr`/`execLst`/`execStr`/`cmd`/`cmdArr`/`cmdLst`/`cmdStr`

Execute any system command. Use `exec`/`cmd` if you have nothing to pipe into stdin, and use `execArr`/`execLst`/`execStr`/`cmdArr`/`cmdLst`/`cmdStr` to pipe some data into the command via stdin.

By default this will capture the output of the command and both return it in an array and print it to the console in real time.

You can specify arguments to the command in the command text (e.g., `exec "dotnet build"`), or you can pass them as additional string parameters  (e.g., `exec ("dotnet", "build", "--help")`), or you can pass them as additional Args parameters (e.g., `exec ("dotnet", _args ["build"; "--help"], _noCapture)`). 

You can pass in string data via stdin, e.g., `cat "file.txt" |> execArr "clip"`.

Available arguments:
- `_silent` - Do not print the output of the command to the console in real time
- `_noCapture` - Do not capture and return the output of the command. This will override `_silent`
- `_args` - A sequence of string arguments to pass to the command

## `|%`

Iterate/map over a sequence, similar to the equivalent operator in PowerShell. E.g., `cmd "git branch" |% _.Trim()`

## `|?`

Filter a sequence, similar to the equivalent operator in PowerShell. E.g., `cmd ("git branch", _silent) |? (not << _.StartsWith("*"))`
