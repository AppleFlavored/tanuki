// using Gtk;

using Tanuki;
using Tanuki.Dom;
using Tanuki.Html;

// Application.Init();
//
// var app = new Application("me.appleflavored.tanuki", GLib.ApplicationFlags.None);
// app.Register(GLib.Cancellable.Current);
//
// var window = new MainWindow();
// app.AddWindow(window);
//
// window.Show();
// Application.Run();

var client = new HttpClient();
var response = await client.GetAsync("https://example.com/");
var content = await response.Content.ReadAsStringAsync();

var parser = new HtmlParser("<!DOCTYPE html><html><head><title>Hello</title><style>Stuff</style></head><body>This is text.</body></html>", ParsingFlags.None);
var document = parser.Parse();

document.Print();