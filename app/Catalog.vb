Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.Json.Serialization

' catalog.json: the list of apps. Each app is a set of binaries written at fixed flash offsets
' (bootloader, partition table, boot_app0, application), hosted as GitHub release assets.
Public Class FlashFile
    <JsonPropertyName("name")> Public Property Name As String = ""
    <JsonPropertyName("offset")> Public Property Offset As String = "0x0"
    <JsonPropertyName("url")> Public Property Url As String = ""
    <JsonPropertyName("sha256")> Public Property Sha256 As String = ""
    <JsonPropertyName("size")> Public Property Size As Long

    Public ReadOnly Property OffsetValue As Long
        Get
            Dim t = Offset.Trim().ToLowerInvariant()
            If t.StartsWith("0x") Then Return Convert.ToInt64(t.Substring(2), 16)
            Return Long.Parse(t)
        End Get
    End Property
End Class

Public Class AppEntry
    <JsonPropertyName("id")> Public Property Id As String = ""
    <JsonPropertyName("name")> Public Property Name As String = ""
    <JsonPropertyName("version")> Public Property Version As String = ""
    <JsonPropertyName("device")> Public Property Device As String = ""
    <JsonPropertyName("chip")> Public Property Chip As String = "esp32s3"
    <JsonPropertyName("category")> Public Property Category As String = ""
    <JsonPropertyName("description")> Public Property Description As String = ""
    <JsonPropertyName("notes")> Public Property Notes As String = ""
    <JsonPropertyName("flash_size")> Public Property FlashSize As String = ""
    ' "default" = esptool resets the board itself; "1200bps" = TinyUSB firmware, touch the port at
    ' 1200 baud first so it jumps into the ROM bootloader (it comes back on another COM port)
    <JsonPropertyName("reset")> Public Property Reset As String = "default"
    <JsonPropertyName("baud")> Public Property Baud As Integer = 921600
    <JsonPropertyName("released")> Public Property Released As String = ""
    <JsonPropertyName("files")> Public Property Files As List(Of FlashFile) = New List(Of FlashFile)

    Public ReadOnly Property TotalSize As Long
        Get
            Return Files.Sum(Function(f) f.Size)
        End Get
    End Property
End Class

Public Class CatalogDoc
    <JsonPropertyName("version")> Public Property Version As Integer = 1
    <JsonPropertyName("updated")> Public Property Updated As String = ""
    <JsonPropertyName("apps")> Public Property Apps As List(Of AppEntry) = New List(Of AppEntry)
End Class

Public Module CatalogIO
    Public Const DefaultCatalogUrl As String = "https://raw.githubusercontent.com/szuszjan/esp32-app-store/main/catalog.json"

    Public ReadOnly Http As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(10)}

    Sub New()
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("ESP32AppStore/1.0")
    End Sub

    Public ReadOnly Property DataDir As String
        Get
            Dim d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ESP32AppStore")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    ' A URL or a local path (for testing a catalog before it is published).
    Public Async Function LoadAsync(source As String) As Task(Of CatalogDoc)
        Dim json As String
        If source.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
            ' cache-buster: raw.githubusercontent.com keeps old copies for a few minutes
            Dim u = source & If(source.Contains("?"), "&", "?") & "t=" & DateTime.UtcNow.Ticks.ToString()
            json = Await Http.GetStringAsync(u)
            File.WriteAllText(Path.Combine(DataDir, "catalog.cache.json"), json)
        Else
            json = File.ReadAllText(source)
        End If
        Dim opts As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}
        Dim doc = JsonSerializer.Deserialize(Of CatalogDoc)(json, opts)
        ' relative file URLs in a local catalog point next to it
        If Not source.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
            Dim baseDir = Path.GetDirectoryName(Path.GetFullPath(source))
            For Each a In doc.Apps
                For Each f In a.Files
                    If Not f.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) AndAlso Not Path.IsPathRooted(f.Url) Then
                        f.Url = Path.Combine(baseDir, f.Url)
                    End If
                Next
            Next
        End If
        Return doc
    End Function

    Public Function Sha256Of(path As String) As String
        Using s = File.OpenRead(path)
            Return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant()
        End Using
    End Function

    ' Downloads (or copies) every file of an app into the cache, checking size and SHA-256.
    ' A cached copy with the right hash is reused.
    Public Async Function FetchFilesAsync(app As AppEntry, progress As Action(Of Long, Long), log As Action(Of String)) As Task(Of List(Of String))
        Dim dir = Path.Combine(DataDir, "cache", app.Id, app.Version)
        Directory.CreateDirectory(dir)
        Dim result As New List(Of String)
        Dim total = Math.Max(1, app.TotalSize), done As Long = 0
        For Each f In app.Files
            Dim target = Path.Combine(dir, f.Name)
            Dim ok = File.Exists(target) AndAlso (f.Sha256 = "" OrElse Sha256Of(target) = f.Sha256.ToLowerInvariant())
            If Not ok Then
                log($"Downloading {f.Name} ({f.Size \ 1024} KB)")
                If f.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                    Using resp = Await Http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead)
                        resp.EnsureSuccessStatusCode()
                        Using src = Await resp.Content.ReadAsStreamAsync(), dst = File.Create(target & ".part")
                            Dim buf(81919) As Byte, n As Integer
                            Do
                                n = Await src.ReadAsync(buf, 0, buf.Length)
                                If n <= 0 Then Exit Do
                                Await dst.WriteAsync(buf, 0, n)
                                done += n
                                progress(done, total)
                            Loop
                        End Using
                    End Using
                    If File.Exists(target) Then File.Delete(target)
                    File.Move(target & ".part", target)
                Else
                    File.Copy(f.Url, target, True)
                    done += f.Size
                    progress(done, total)
                End If
                If f.Sha256 <> "" AndAlso Sha256Of(target) <> f.Sha256.ToLowerInvariant() Then
                    File.Delete(target)
                    Throw New InvalidDataException($"{f.Name}: checksum mismatch, the download is damaged")
                End If
            Else
                done += f.Size
                progress(done, total)
                log($"{f.Name}: cached, checksum OK")
            End If
            result.Add(target)
        Next
        Return result
    End Function
End Module
