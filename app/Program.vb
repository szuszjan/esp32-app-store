Friend Module Program

    ' Optional first argument: a catalog URL or local catalog.json (for testing before publishing).
    <STAThread()>
    Friend Function Main(args As String()) As Integer
        If args.Length > 0 AndAlso args(0) = "--install" Then Return Installer.RunCommandLine(args)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, Sub(s, e)
                                                    IO.File.AppendAllText(IO.Path.Combine(CatalogIO.DataDir, "ui.log"), DateTime.Now.ToString("s") & " " & e.Exception.ToString() & vbCrLf)
                                                    MessageBox.Show(e.Exception.Message, "ESP32 App Store", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                                End Sub
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm(If(args.Length > 0, args(0), Nothing)))
        Return 0
    End Function

End Module
