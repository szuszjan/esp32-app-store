Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Text

Public Class MainForm
    Inherits Form

    ' ---- look ----
    Private Shared ReadOnly CBg As Color = Color.FromArgb(22, 24, 29)
    Private Shared ReadOnly CPanel As Color = Color.FromArgb(30, 33, 40)
    Private Shared ReadOnly CCard As Color = Color.FromArgb(38, 42, 51)
    Private Shared ReadOnly CSel As Color = Color.FromArgb(40, 66, 102)
    Private Shared ReadOnly CAcc As Color = Color.FromArgb(61, 155, 255)
    Private Shared ReadOnly CText As Color = Color.FromArgb(236, 239, 244)
    Private Shared ReadOnly CDim As Color = Color.FromArgb(154, 163, 175)
    Private Shared ReadOnly CWarn As Color = Color.FromArgb(255, 196, 87)
    Private Shared ReadOnly COk As Color = Color.FromArgb(80, 210, 120)
    Private Shared ReadOnly CErr As Color = Color.FromArgb(255, 99, 99)

    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly cboDevice As New ComboBox()
    Private ReadOnly btnRefresh As New Button()
    Private ReadOnly btnMenu As New Button()
    Private ReadOnly lstApps As New ListBox()
    Private ReadOnly lblName As New Label(), lblMeta As New Label(), lblDesc As New Label(), lblNotes As New Label(), lblFiles As New Label()
    Private ReadOnly cboPort As New ComboBox()
    Private ReadOnly btnPorts As New Button()
    Private ReadOnly chkErase As New CheckBox()
    Private ReadOnly btnInstall As New Button()
    Private ReadOnly bar As New ProgressBar()
    Private ReadOnly lblStatus As New Label()
    Private ReadOnly txtLog As New TextBox()
    Private ReadOnly btnLog As New Button()
    Private ReadOnly logPanel As New Panel()

    Private catalog As New CatalogDoc()
    Private catalogSource As String = CatalogIO.DefaultCatalogUrl
    Private busy As Boolean = False
    Private ReadOnly settingsPath As String = Path.Combine(CatalogIO.DataDir, "settings.txt")

    Public Sub New(Optional sourceOverride As String = Nothing)
        Text = "ESP32 App Store"
        Size = New Size(1060, 700)
        MinimumSize = New Size(860, 560)
        StartPosition = FormStartPosition.CenterScreen
        BackColor = CBg
        ForeColor = CText
        Font = New Font("Segoe UI", 9.5F)
        DoubleBuffered = True
        If File.Exists(settingsPath) Then
            Dim s = File.ReadAllText(settingsPath).Trim()
            If s <> "" Then catalogSource = s
        End If
        If Not String.IsNullOrEmpty(sourceOverride) Then catalogSource = sourceOverride
        BuildUi()
    End Sub

    ' ------------------------------------------------------------------ layout
    Private Sub StyleButton(b As Button, text As String, Optional accent As Boolean = False)
        b.Text = text
        b.FlatStyle = FlatStyle.Flat
        b.FlatAppearance.BorderSize = 0
        b.BackColor = If(accent, CAcc, CCard)
        b.ForeColor = If(accent, Color.White, CText)
        b.Cursor = Cursors.Hand
        b.AutoSize = False
    End Sub

    Private Sub BuildUi()
        ' top bar
        Dim top As New Panel With {.Dock = DockStyle.Top, .Height = 60, .BackColor = CPanel, .Padding = New Padding(16, 14, 16, 12)}
        Dim title As New Label With {.Text = "ESP32 App Store", .Font = New Font("Segoe UI Semibold", 15.0F), .ForeColor = CText, .AutoSize = True, .Location = New Point(56, 15)}
        Dim logo As New PictureBox With {.Size = New Size(30, 30), .Location = New Point(16, 15)}
        AddHandler logo.Paint, Sub(s, e) DrawLogo(e.Graphics, New Rectangle(0, 0, 30, 30))
        txtSearch.PlaceholderText = "Search apps..."
        txtSearch.BorderStyle = BorderStyle.FixedSingle
        txtSearch.BackColor = CCard : txtSearch.ForeColor = CText
        txtSearch.Width = 230 : txtSearch.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        cboDevice.DropDownStyle = ComboBoxStyle.DropDownList
        cboDevice.FlatStyle = FlatStyle.Flat : cboDevice.BackColor = CCard : cboDevice.ForeColor = CText
        cboDevice.Width = 190 : cboDevice.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        StyleButton(btnRefresh, "Refresh") : btnRefresh.Size = New Size(80, 28) : btnRefresh.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        StyleButton(btnMenu, "...") : btnMenu.Size = New Size(36, 28) : btnMenu.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        top.Controls.AddRange({logo, title, txtSearch, cboDevice, btnRefresh, btnMenu})
        AddHandler top.Resize, Sub()
                                   Dim r = top.ClientSize.Width - 16
                                   btnMenu.Location = New Point(r - 36, 16) : r -= 44
                                   btnRefresh.Location = New Point(r - 80, 16) : r -= 88
                                   cboDevice.Location = New Point(r - 190, 18) : r -= 198
                                   txtSearch.Location = New Point(r - 230, 18)
                               End Sub

        ' app list (owner-drawn cards)
        lstApps.Dock = DockStyle.Left
        lstApps.Width = 360
        lstApps.BorderStyle = BorderStyle.None
        lstApps.BackColor = CBg
        lstApps.DrawMode = DrawMode.OwnerDrawFixed
        lstApps.ItemHeight = 72
        lstApps.IntegralHeight = False
        AddHandler lstApps.DrawItem, AddressOf DrawAppItem
        AddHandler lstApps.SelectedIndexChanged, Sub() ShowDetails()

        ' details
        Dim detail As New Panel With {.Dock = DockStyle.Fill, .BackColor = CBg, .Padding = New Padding(28, 22, 28, 10), .AutoScroll = True}
        lblName.Font = New Font("Segoe UI Semibold", 20.0F) : lblName.ForeColor = CText : lblName.AutoSize = True
        lblMeta.ForeColor = CDim : lblMeta.AutoSize = True
        lblDesc.ForeColor = CText : lblDesc.Font = New Font("Segoe UI", 10.5F)
        lblNotes.ForeColor = CWarn
        lblFiles.ForeColor = CDim : lblFiles.Font = New Font("Consolas", 9.0F)
        detail.Controls.AddRange({lblName, lblMeta, lblDesc, lblNotes, lblFiles})
        AddHandler detail.Resize, Sub() LayoutDetail(detail)

        ' install bar
        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 96, .BackColor = CPanel, .Padding = New Padding(16, 12, 16, 10)}
        Dim lblPort As New Label With {.Text = "Port", .ForeColor = CDim, .AutoSize = True, .Location = New Point(16, 20)}
        cboPort.DropDownStyle = ComboBoxStyle.DropDownList
        cboPort.FlatStyle = FlatStyle.Flat : cboPort.BackColor = CCard : cboPort.ForeColor = CText
        cboPort.Location = New Point(56, 16) : cboPort.Width = 380
        StyleButton(btnPorts, "Rescan") : btnPorts.Location = New Point(444, 15) : btnPorts.Size = New Size(76, 28)
        chkErase.Text = "Erase all flash first" : chkErase.ForeColor = CDim : chkErase.AutoSize = True : chkErase.Location = New Point(534, 20)
        StyleButton(btnInstall, "Install", True) : btnInstall.Font = New Font("Segoe UI Semibold", 11.0F)
        btnInstall.Size = New Size(150, 40) : btnInstall.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        bar.Location = New Point(16, 58) : bar.Height = 8 : bar.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        bar.Style = ProgressBarStyle.Continuous
        lblStatus.ForeColor = CDim : lblStatus.AutoSize = False : lblStatus.Location = New Point(16, 70) : lblStatus.Height = 20 : lblStatus.Width = 800
        lblStatus.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        StyleButton(btnLog, "Log") : btnLog.Size = New Size(60, 24) : btnLog.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        bottom.Controls.AddRange({lblPort, cboPort, btnPorts, chkErase, btnInstall, bar, lblStatus, btnLog})
        AddHandler bottom.Resize, Sub()
                                      Dim w = bottom.ClientSize.Width
                                      btnInstall.Location = New Point(w - 166, 12)
                                      btnLog.Location = New Point(w - 76, 64)
                                      bar.Width = w - 32 - 170
                                      lblStatus.Width = w - 32 - 90
                                  End Sub

        ' log (hidden until asked for or something fails)
        logPanel.Dock = DockStyle.Bottom : logPanel.Height = 170 : logPanel.Visible = False : logPanel.Padding = New Padding(0)
        txtLog.Multiline = True : txtLog.ReadOnly = True : txtLog.ScrollBars = ScrollBars.Vertical : txtLog.Dock = DockStyle.Fill
        txtLog.BackColor = Color.FromArgb(14, 15, 18) : txtLog.ForeColor = Color.FromArgb(190, 198, 210) : txtLog.BorderStyle = BorderStyle.None
        txtLog.Font = New Font("Consolas", 9.0F)
        logPanel.Controls.Add(txtLog)

        Controls.Add(detail)
        Controls.Add(lstApps)
        Controls.Add(logPanel)
        Controls.Add(bottom)
        Controls.Add(top)

        AddHandler txtSearch.TextChanged, Sub() FillList()
        AddHandler cboDevice.SelectedIndexChanged, Sub() FillList()
        AddHandler btnRefresh.Click, Async Sub() Await LoadCatalogAsync()
        AddHandler btnPorts.Click, Sub() FillPorts()
        AddHandler btnInstall.Click, Async Sub() Await InstallAsync()
        AddHandler btnLog.Click, Sub() logPanel.Visible = Not logPanel.Visible
        AddHandler btnMenu.Click, Sub() ShowMenu()
        AddHandler Shown, Async Sub()
                              Try
                                  FillPorts()
                              Catch ex As Exception
                                  Log("Port list failed: " & ex.ToString())
                              End Try
                              Await LoadCatalogAsync()
                          End Sub
    End Sub

    Private Sub LayoutDetail(detail As Panel)
        Dim w = Math.Max(200, detail.ClientSize.Width - 56)
        Dim y = 22
        lblName.Location = New Point(28, y) : y += lblName.Height + 2
        lblMeta.Location = New Point(30, y) : y += lblMeta.Height + 16
        lblDesc.MaximumSize = New Size(w, 0) : lblDesc.AutoSize = True : lblDesc.Location = New Point(28, y) : y += lblDesc.Height + 14
        lblNotes.MaximumSize = New Size(w, 0) : lblNotes.AutoSize = True : lblNotes.Location = New Point(28, y) : y += If(lblNotes.Text = "", 0, lblNotes.Height + 14)
        lblFiles.MaximumSize = New Size(w, 0) : lblFiles.AutoSize = True : lblFiles.Location = New Point(28, y)
    End Sub

    ' ------------------------------------------------------------------ drawing
    Private Shared Function CategoryColor(s As String) As Color
        Select Case If(s, "").ToLowerInvariant()
            Case "games" : Return Color.FromArgb(255, 140, 66)
            Case "tools" : Return Color.FromArgb(61, 155, 255)
            Case "system" : Return Color.FromArgb(170, 110, 255)
            Case "media" : Return Color.FromArgb(80, 200, 140)
            Case "camera" : Return Color.FromArgb(255, 90, 120)
            Case "wearable" : Return Color.FromArgb(70, 200, 220)
        End Select
        Dim palette = {Color.FromArgb(61, 155, 255), Color.FromArgb(255, 140, 66), Color.FromArgb(80, 200, 140), Color.FromArgb(190, 110, 255),
                       Color.FromArgb(255, 90, 120), Color.FromArgb(255, 200, 70), Color.FromArgb(70, 200, 220)}
        Dim h As Long = 0
        For Each c In If(s, "") : h = (h * 31 + AscW(c)) And &H7FFFFFFFL : Next
        Return palette(CInt(h Mod palette.Length))
    End Function

    Private Shared Function RoundRect(r As Rectangle, radius As Integer) As GraphicsPath
        Dim p As New GraphicsPath(), d = radius * 2
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    Private Shared Sub DrawLogo(g As Graphics, r As Rectangle)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using p = RoundRect(New Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 7), b As New LinearGradientBrush(r, Color.FromArgb(61, 155, 255), Color.FromArgb(120, 90, 255), 45.0F)
            g.FillPath(b, p)
        End Using
        ' a chip: square body with pins
        Dim c = New Rectangle(r.X + 9, r.Y + 9, r.Width - 18, r.Height - 18)
        Using pen As New Pen(Color.White, 2)
            g.DrawRectangle(pen, c)
            For i = 0 To 2
                Dim o = c.X + 2 + i * (c.Width - 4) \ 2
                g.DrawLine(pen, o, c.Y - 4, o, c.Y - 1) : g.DrawLine(pen, o, c.Bottom + 1, o, c.Bottom + 4)
                Dim v = c.Y + 2 + i * (c.Height - 4) \ 2
                g.DrawLine(pen, c.X - 4, v, c.X - 1, v) : g.DrawLine(pen, c.Right + 1, v, c.Right + 4, v)
            Next
        End Using
    End Sub

    Private Sub DrawAppItem(sender As Object, e As DrawItemEventArgs)
        e.Graphics.FillRectangle(New SolidBrush(CBg), e.Bounds)
        If e.Index < 0 OrElse e.Index >= lstApps.Items.Count Then Return
        Dim a = DirectCast(lstApps.Items(e.Index), AppEntry)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        Dim sel = (e.State And DrawItemState.Selected) = DrawItemState.Selected
        Dim card = New Rectangle(e.Bounds.X + 10, e.Bounds.Y + 5, e.Bounds.Width - 20, e.Bounds.Height - 10)
        Using p = RoundRect(card, 8), b As New SolidBrush(If(sel, CSel, CCard))
            g.FillPath(b, p)
        End Using
        If sel Then
            Using b As New SolidBrush(CAcc) : g.FillRectangle(b, card.X, card.Y + 12, 3, card.Height - 24) : End Using
        End If
        ' icon: coloured tile with initials
        Dim icon = New Rectangle(card.X + 12, card.Y + (card.Height - 40) \ 2, 40, 40)
        Using p = RoundRect(icon, 9), b As New SolidBrush(CategoryColor(a.Category))
            g.FillPath(b, p)
        End Using
        Dim ini = String.Concat(a.Name.Split({" "c}, StringSplitOptions.RemoveEmptyEntries).Where(Function(w) Char.IsLetterOrDigit(w.Trim("("c, ")"c).FirstOrDefault())).Take(2).Select(Function(w) Char.ToUpperInvariant(w.Trim("("c, ")"c)(0))))
        Using f As New Font("Segoe UI Semibold", 12.0F)
            Dim sz = g.MeasureString(ini, f)
            g.DrawString(ini, f, Brushes.White, icon.X + (icon.Width - sz.Width) / 2, icon.Y + (icon.Height - sz.Height) / 2)
        End Using
        Using fName As New Font("Segoe UI Semibold", 11.0F), fSub As New Font("Segoe UI", 9.0F), bt As New SolidBrush(CText), bd As New SolidBrush(CDim)
            g.DrawString(a.Name, fName, bt, icon.Right + 12, card.Y + 12)
            g.DrawString($"{a.Device}   v{a.Version}", fSub, bd, icon.Right + 13, card.Y + 36)
        End Using
        ' category chip on the right
        If a.Category <> "" Then
            Using f As New Font("Segoe UI", 8.0F)
                Dim sz = g.MeasureString(a.Category, f)
                Dim chip = New Rectangle(CInt(card.Right - sz.Width - 22), card.Y + 12, CInt(sz.Width + 12), 18)
                Using p = RoundRect(chip, 9), b As New SolidBrush(Color.FromArgb(60, CategoryColor(a.Category)))
                    g.FillPath(b, p)
                End Using
                Using b As New SolidBrush(CategoryColor(a.Category)) : g.DrawString(a.Category, f, b, chip.X + 6, chip.Y + 2) : End Using
            End Using
        End If
    End Sub

    ' ------------------------------------------------------------------ data
    Private Async Function LoadCatalogAsync() As Task
        SetStatus("Loading catalog...", CDim)
        Try
            catalog = Await CatalogIO.LoadAsync(catalogSource)
            Dim cur = TryCast(cboDevice.SelectedItem, String)
            cboDevice.Items.Clear()
            cboDevice.Items.Add("All devices")
            For Each d In catalog.Apps.Select(Function(a) a.Device).Distinct().OrderBy(Function(x) x) : cboDevice.Items.Add(d) : Next
            cboDevice.SelectedItem = If(cur IsNot Nothing AndAlso cboDevice.Items.Contains(cur), cur, "All devices")
            FillList()
            SetStatus($"{catalog.Apps.Count} apps   (catalog {If(catalog.Updated = "", "", "from " & catalog.Updated)})", CDim)
        Catch ex As Exception
            SetStatus("Could not load the catalog: " & ex.Message, CErr)
            Log("Catalog source: " & catalogSource)
            Log(ex.ToString())
        End Try
    End Function

    Private Sub FillList()
        Dim selId = TryCast(lstApps.SelectedItem, AppEntry)?.Id
        Dim q = txtSearch.Text.Trim().ToLowerInvariant()
        Dim dev = TryCast(cboDevice.SelectedItem, String)
        lstApps.BeginUpdate()
        lstApps.Items.Clear()
        For Each a In catalog.Apps.OrderBy(Function(x) x.Name)
            If dev IsNot Nothing AndAlso dev <> "All devices" AndAlso a.Device <> dev Then Continue For
            If q <> "" AndAlso Not (a.Name & " " & a.Description & " " & a.Category & " " & a.Device).ToLowerInvariant().Contains(q) Then Continue For
            lstApps.Items.Add(a)
            If a.Id = selId Then lstApps.SelectedIndex = lstApps.Items.Count - 1
        Next
        lstApps.EndUpdate()
        If lstApps.SelectedIndex < 0 AndAlso lstApps.Items.Count > 0 Then lstApps.SelectedIndex = 0
        ShowDetails()
    End Sub

    Private Sub ShowDetails()
        Dim a = TryCast(lstApps.SelectedItem, AppEntry)
        If a Is Nothing Then
            lblName.Text = "No app selected" : lblMeta.Text = "" : lblDesc.Text = "" : lblNotes.Text = "" : lblFiles.Text = ""
            btnInstall.Enabled = False
        Else
            lblName.Text = a.Name
            lblMeta.Text = $"v{a.Version}   ·   {a.Device}   ·   {a.Chip.ToUpperInvariant()}" &
                           If(a.FlashSize <> "", "   ·   flash " & a.FlashSize, "") & $"   ·   {a.TotalSize / 1048576.0:0.0} MB" &
                           If(a.Released <> "", "   ·   " & a.Released, "")
            lblDesc.Text = a.Description
            lblNotes.Text = If(a.Notes = "", "", "Note: " & a.Notes) &
                            If(a.Reset = "1200bps", If(a.Notes = "", "", vbCrLf) & "Resets into the bootloader with a 1200 bps touch (TinyUSB firmware).", "")
            Dim sb As New StringBuilder("Files written:" & vbCrLf)
            For Each f In a.Files.OrderBy(Function(x) x.OffsetValue)
                sb.AppendLine($"  {f.Offset,-9} {f.Name,-18} {f.Size / 1024.0,8:0.0} KB")
            Next
            lblFiles.Text = sb.ToString()
            btnInstall.Enabled = Not busy
        End If
        LayoutDetail(DirectCast(lblName.Parent, Panel))
    End Sub

    Private Sub FillPorts()
        Dim cur = TryCast(cboPort.SelectedItem, PortInfo)?.Name
        cboPort.Items.Clear()
        For Each p In Ports.List() : cboPort.Items.Add(p) : Next
        For i = 0 To cboPort.Items.Count - 1
            If DirectCast(cboPort.Items(i), PortInfo).Name = cur Then cboPort.SelectedIndex = i
        Next
        If cboPort.SelectedIndex < 0 AndAlso cboPort.Items.Count > 0 Then cboPort.SelectedIndex = 0
    End Sub

    Private Sub ShowMenu()
        Dim m As New ContextMenuStrip()
        m.Items.Add("Open a local catalog.json...", Nothing, Async Sub()
                                                                  Using d As New OpenFileDialog With {.Filter = "Catalog (*.json)|*.json"}
                                                                      If d.ShowDialog(Me) = DialogResult.OK Then
                                                                          catalogSource = d.FileName : File.WriteAllText(settingsPath, catalogSource)
                                                                          Await LoadCatalogAsync()
                                                                      End If
                                                                  End Using
                                                              End Sub)
        m.Items.Add("Use the online catalog (GitHub)", Nothing, Async Sub()
                                                                    catalogSource = CatalogIO.DefaultCatalogUrl
                                                                    If File.Exists(settingsPath) Then File.Delete(settingsPath)
                                                                    Await LoadCatalogAsync()
                                                                End Sub)
        m.Items.Add("Open the download cache", Nothing, Sub() Process.Start("explorer.exe", CatalogIO.DataDir))
        m.Items.Add(New ToolStripSeparator())
        m.Items.Add("Catalog: " & catalogSource).Enabled = False
        m.Show(btnMenu, New Point(0, btnMenu.Height))
    End Sub

    ' ------------------------------------------------------------------ install
    Private Sub Log(s As String)
        If InvokeRequired Then BeginInvoke(Sub() Log(s)) : Return
        txtLog.AppendText(s & vbCrLf)
        Try
            File.AppendAllText(Path.Combine(CatalogIO.DataDir, "ui.log"), s & vbCrLf)
        Catch
        End Try
    End Sub

    Private Sub SetStatus(s As String, c As Color)
        If InvokeRequired Then BeginInvoke(Sub() SetStatus(s, c)) : Return
        lblStatus.Text = s : lblStatus.ForeColor = c
    End Sub

    Private Sub SetProgress(v As Double)
        If InvokeRequired Then BeginInvoke(Sub() SetProgress(v)) : Return
        bar.Value = CInt(Math.Max(0, Math.Min(1000, v * 1000)))
    End Sub

    Private Sub SetBusy(b As Boolean)
        busy = b
        btnInstall.Enabled = Not b AndAlso lstApps.SelectedItem IsNot Nothing
        btnInstall.Text = If(b, "Installing...", "Install")
        lstApps.Enabled = Not b : cboPort.Enabled = Not b : btnPorts.Enabled = Not b : btnRefresh.Enabled = Not b
    End Sub

    Private Async Function InstallAsync() As Task
        Dim a = TryCast(lstApps.SelectedItem, AppEntry)
        Dim p = TryCast(cboPort.SelectedItem, PortInfo)
        If a Is Nothing Then Return
        If p Is Nothing Then MessageBox.Show(Me, "Plug the device in and pick its port.", "No port", MessageBoxButtons.OK, MessageBoxIcon.Information) : Return
        If MessageBox.Show(Me, $"Install {a.Name} v{a.Version} on {p.Name}?" & vbCrLf & vbCrLf &
                           "This replaces the firmware on the device. Files it keeps in its own flash storage can be lost " &
                           "when the new app uses a different partition layout." & If(chkErase.Checked, vbCrLf & vbCrLf & "Erase all flash first is ON: everything on the chip is wiped.", ""),
                           "Install", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        bar.Maximum = 1000
        SetBusy(True)
        txtLog.Clear()
        Dim sw = Stopwatch.StartNew()
        Try
            Dim port = p.Name
            Do
                Dim r = Await Installer.InstallAsync(a, port, chkErase.Checked, AddressOf Log, AddressOf SetProgress, Sub(s) SetStatus(s, CDim))
                If r.Ok Then
                    SetStatus($"Installed {a.Name} v{a.Version} on {r.Port} in {sw.Elapsed.TotalSeconds:0} s. The device restarts by itself.", COk)
                    Exit Do
                End If
                logPanel.Visible = True
                Dim hint = If(r.NeedsDownloadMode,
                    "The board did not enter download mode." & vbCrLf & vbCrLf &
                    "Hold the BOOT / G0 button, plug the USB cable in (or press reset), let go, pick the port again and press Retry.",
                    If(r.PortBusy, "The port is busy: close the Arduino IDE serial monitor or any other program using it, then Retry.",
                       "esptool failed (exit code " & r.ExitCode & "). See the log."))
                SetStatus("Install failed", CErr)
                If MessageBox.Show(Me, hint, "Install failed", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) <> DialogResult.Retry Then Exit Do
                FillPorts()
                Dim sp = TryCast(cboPort.SelectedItem, PortInfo)
                If sp IsNot Nothing Then port = sp.Name
            Loop
        Catch ex As Exception
            SetStatus("Error: " & ex.Message, CErr)
            Log(ex.ToString())
            logPanel.Visible = True
        Finally
            SetBusy(False)
            FillPorts()
        End Try
    End Function
End Class
