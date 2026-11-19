using System.IO;
using System.Windows;
using FloatingDock;

static class Program
{
    [STAThread] static int Main(string[] args)
    {
        int failures=0;
        void Check(string name,Action action){try{action();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex);}}
        void Equal<T>(T want,T got){if(!Equals(want,got))throw new Exception($"expected {want}, got {got}");}
        var root=Path.Combine(Directory.GetCurrentDirectory(),"work","interaction-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            Check("file creation and rename notify without waiting for polling",()=>{
                var dir=Path.Combine(root,"watch");Directory.CreateDirectory(dir);using var signal=new ManualResetEventSlim();
                using var monitor=new FileChangeMonitor([new(dir,true)],()=>signal.Set());
                var file=Path.Combine(dir,"新下载.txt");File.WriteAllText(file,"test");if(!signal.Wait(1500))throw new Exception("creation notification exceeded 1.5s");
                signal.Reset();File.Move(file,Path.Combine(dir,"已改名.txt"));if(!signal.Wait(1500))throw new Exception("rename notification missing");
            });
            Check("nested changes notify but disposed monitors stop",()=>{
                var dir=Path.Combine(root,"nested");var sub=Path.Combine(dir,"sub");Directory.CreateDirectory(sub);using var signal=new ManualResetEventSlim();
                var monitor=new FileChangeMonitor([new(dir,true)],()=>signal.Set());File.WriteAllText(Path.Combine(sub,"file.txt"),"test");if(!signal.Wait(1500))throw new Exception("recursive notification missing");
                monitor.Dispose();Thread.Sleep(350);signal.Reset();File.WriteAllText(Path.Combine(sub,"later.txt"),"test");if(signal.Wait(500))throw new Exception("notification after dispose");
            });
            Check("continuous download writes cannot postpone notifications indefinitely",()=>{
                var dir=Path.Combine(root,"continuous");Directory.CreateDirectory(dir);using var signal=new ManualResetEventSlim();
                using var monitor=new FileChangeMonitor([new(dir,false)],()=>signal.Set());
                var writer=Task.Run(()=>{for(int i=0;i<18;i++){File.WriteAllText(Path.Combine(dir,"active.part"),i.ToString());Thread.Sleep(45);}});
                bool timely=signal.Wait(700);writer.GetAwaiter().GetResult();Equal(true,timely);
            });
            Check("watched new desktop shortcut imports directly from its original location",()=>{
                var dir=Path.Combine(root,"desktop-fixture");Directory.CreateDirectory(dir);var state=new DockState();using var signal=new ManualResetEventSlim();
                using var monitor=new FileChangeMonitor([new(dir,false)],()=>{DockCatalog.ImportDesktop(state,Directory.EnumerateFiles(dir,"*.lnk"));signal.Set();});
                var shortcut=Path.Combine(dir,"新安装软件.lnk");File.WriteAllText(shortcut,"test shortcut fixture");
                if(!signal.Wait(1500))throw new Exception("new shortcut not imported promptly");Equal(shortcut,state.Items.Single().Path);Equal(true,File.Exists(shortcut));
            });
            Check("Windows selection preserves ctrl toggles and shift ranges",()=>{
                string[] visible=["a","b","c","d"];var selection=new FileSelection();selection.Select("b",visible,false,false);selection.Select("d",visible,false,true);Equal("b,c,d",string.Join(',',visible.Where(selection.Contains)));
                selection.Select("c",visible,true,false);Equal("b,d",string.Join(',',visible.Where(selection.Contains)));selection.Select("b",visible,false,false,true);Equal(2,selection.Count);
                selection.Select("b",visible,true,false,true);Equal(2,selection.Count);
                selection.Select("b",visible,true,false);Equal(1,selection.Count);
                selection.SelectAll(visible);Equal(4,selection.Count);selection.Retain(["a","b"]);Equal(2,selection.Count);
            });
            Check("desktop auto import keeps ignored paths and preserves existing names",()=>{
                var a=Path.Combine(root,"A.lnk");var b=Path.Combine(root,"B.lnk");var c=Path.Combine(root,"C.lnk");
                var state=new DockState{IgnoredDesktopShortcuts=[b]};state.Items.Add(new(){Path=a,Name="自定义名称"});
                Equal(1,DockCatalog.ImportDesktop(state,[a,b,c,c]));Equal(2,state.Items.Count);Equal("自定义名称",state.Items[0].Name);
                var save=Path.Combine(root,"settings.json");DockStore.Save(save,state);Equal(b,DockStore.Load(save).IgnoredDesktopShortcuts.Single());
                state.AutoImportDesktop=false;Equal(0,DockCatalog.ImportDesktop(state,[Path.Combine(root,"D.lnk")]));
            });
            if(!args.Contains("--model-only"))
            foreach(var item in new[]{("default",0u,DragDropEffects.Move),("copy",8u,DragDropEffects.Copy),("move",4u,DragDropEffects.Move),("link",12u,DragDropEffects.Link)})
                Check("native Windows target performs "+item.Item1+" with real file semantics",()=>{
                    var source=Path.Combine(root,item.Item1+"-source");var dest=Path.Combine(root,item.Item1+"-dest");Directory.CreateDirectory(source);Directory.CreateDirectory(dest);
                    var path=Path.Combine(source,"中文 文件.txt");File.WriteAllText(path,"native data");using var nativeData=ShellDragData.ForFiles([path]);var data=nativeData.Data;
                    Console.WriteLine("INFO native target begin "+item.Item1);
                    using var drop=new ShellFileDrop(dest,data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link,item.Item2|1);
                    Console.WriteLine("INFO native target entered "+drop.Effect);
                    Equal(item.Item3,drop.Effect);var reported=drop.Drop(item.Item2);
                    // An optimized Shell move may return NONE because Shell already moved
                    // the source; the real file assertions below establish completion.
                    if(reported!=item.Item3&&reported!=DragDropEffects.None)throw new Exception("unexpected native completion effect: "+reported);
                    Console.WriteLine("INFO native drop returned");
                    var deadline=DateTime.UtcNow.AddSeconds(4);while(!Directory.EnumerateFileSystemEntries(dest).Any()&&DateTime.UtcNow<deadline)Thread.Sleep(50);
                    if(item.Item3==DragDropEffects.Link){Equal(true,Directory.EnumerateFiles(dest,"*.lnk").Any());Equal(true,File.Exists(path));}
                    else {Equal("native data",File.ReadAllText(Path.Combine(dest,"中文 文件.txt")));Equal(item.Item3!=DragDropEffects.Move,File.Exists(path));}
                });
            if(!args.Contains("--model-only"))Check("native multi-file drop preserves files from different parent folders",()=>{
                var a=Path.Combine(root,"multi-a");var b=Path.Combine(root,"multi-b");var dest=Path.Combine(root,"multi-dest");
                Directory.CreateDirectory(a);Directory.CreateDirectory(b);Directory.CreateDirectory(dest);
                var one=Path.Combine(a,"甲.txt");var two=Path.Combine(b,"乙.txt");File.WriteAllText(one,"one");File.WriteAllText(two,"two");
                using var data=ShellDragData.ForFiles([one,two]);using var drop=new ShellFileDrop(dest,data.Data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link,9);drop.Drop(8);
                Equal("one",File.ReadAllText(Path.Combine(dest,"甲.txt")));Equal("two",File.ReadAllText(Path.Combine(dest,"乙.txt")));Equal(true,File.Exists(one));Equal(true,File.Exists(two));
            });
        }
        finally {Directory.Delete(root,true);}
        Console.WriteLine($"Failures: {failures}");return failures==0?0:1;
    }
}
