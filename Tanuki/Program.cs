using Gtk;
using Tanuki;
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

var parser = new HtmlParser(
    "<!DOCTYPE html><html><head><title>Document</title></head><body><h1>Hello!</h1><p>This is text.</p><p>h</p></body></html>",
    ParsingFlags.None);
var document = parser.Parse();

document.Print();