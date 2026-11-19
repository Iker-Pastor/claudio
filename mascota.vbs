' Lanza la mascota sin ventana de consola.
' La primera vez (o si mascota.cs ha cambiado) la compila a mascota.exe con el
' compilador de C# que ya trae Windows (.NET Framework 4), sin instalar nada.
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
src = dir & "\mascota.cs"
exe = dir & "\mascota.exe"
png = dir & "\clawitzer-shiny.png"
png2 = dir & "\clawitzer.png"

needBuild = Not fso.FileExists(exe)
If Not needBuild Then needBuild = fso.GetFile(src).DateLastModified > fso.GetFile(exe).DateLastModified
If Not needBuild Then needBuild = fso.GetFile(png).DateLastModified > fso.GetFile(exe).DateLastModified
If Not needBuild Then needBuild = fso.GetFile(png2).DateLastModified > fso.GetFile(exe).DateLastModified

If needBuild Then
    win = sh.ExpandEnvironmentStrings("%WINDIR%")
    csc = win & "\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    If Not fso.FileExists(csc) Then csc = win & "\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    rc = sh.Run("""" & csc & """ /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:""" & png & """,clawitzer-shiny.png /resource:""" & png2 & """,clawitzer.png /out:""" & exe & """ """ & src & """", 0, True)
    If rc <> 0 Then
        MsgBox "No se pudo compilar mascota.cs (codigo " & rc & ")." & vbCrLf & _
               "Si la mascota ya esta abierta, cierrala y vuelve a intentarlo.", 16, "Mascota"
        WScript.Quit 1
    End If
End If

sh.Run """" & exe & """", 1, False
