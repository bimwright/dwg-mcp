// Run through dwg_send_code in a disposable host session. This one-shot UI observation
// never changes the drawing. Issue the command under test within one second after scheduling.
// AutoCAD has no WPF Application.Current; inspect live PresentationSources instead.
var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwg-toast-host-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json");
var workerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
var notBefore = DateTime.UtcNow.AddSeconds(1); var deadline = DateTime.UtcNow.AddSeconds(10);
EventHandler observe = null;
observe = (s, e) => {
    try {
        if(DateTime.UtcNow < notBefore) return;
        var windows = System.Windows.PresentationSource.CurrentSources.Cast<System.Windows.PresentationSource>().Select(p => p.RootVisual).OfType<System.Windows.Window>().Where(w => w.GetType().Name == "McpToastWindow").ToList();
        if (windows.Count == 0 && DateTime.UtcNow < deadline) return;
        Autodesk.AutoCAD.ApplicationServices.Application.Idle -= observe;
        var cards = new List<object>();
        foreach (var window in windows) {
            var texts = new List<string>(); var imageWidths = new List<int>(); var pending = new Stack<System.Windows.DependencyObject>(); pending.Push(window);
            while (pending.Count > 0) { var item = pending.Pop(); var picture = item as System.Windows.Controls.Image; var bitmap = picture?.Source as System.Windows.Media.Imaging.BitmapSource; if (bitmap != null) imageWidths.Add(bitmap.PixelWidth); var text = item as System.Windows.Controls.TextBlock; if (text != null) texts.Add(text.Text); for (int i=0; i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(item); i++) pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(item,i)); }
            cards.Add(new { visible=window.IsVisible, active=window.IsActive, showActivated=window.ShowActivated, owner=new System.Windows.Interop.WindowInteropHelper(window).Owner.ToInt64(), window.Left, window.Top, texts, imageWidths });
        }
        var ribbon=Autodesk.Windows.ComponentManager.Ribbon;
        var tab=ribbon==null?null:ribbon.FindTab("BIMWRIGHT_DWG_TAB");
        System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new { hasWpfApplication=System.Windows.Application.Current!=null, workerThread, uiThread=System.Threading.Thread.CurrentThread.ManagedThreadId, mainWindow=Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle.ToInt64(), ribbonTabs=ribbon==null?0:ribbon.Tabs.Count(t=>t.Id=="BIMWRIGHT_DWG_TAB"), buttons=tab==null?new string[0]:tab.Panels.SelectMany(p=>p.Source.Items).Where(i=>i.Id=="BIMWRIGHT_DWG_TOAST").Select(i=>i.Text).ToArray(), cards }, Newtonsoft.Json.Formatting.Indented));
    } catch (Exception ex) { Autodesk.AutoCAD.ApplicationServices.Application.Idle -= observe; System.IO.File.WriteAllText(output, ex.ToString()); }
};
Autodesk.AutoCAD.ApplicationServices.Application.Idle += observe;
return new { scheduled = true, evidence_file = output };
