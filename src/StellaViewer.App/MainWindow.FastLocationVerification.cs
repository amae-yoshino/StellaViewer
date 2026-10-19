using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyLocationNavigationRegressionsAsync(string imagePath,string reportPath)
    {
        WatchFoldersEnabled=false;ShowActivated=false;ShowInTaskbar=false;Left=Top=-32000;Show();
        await OpenAsync(imagePath);await activeDisplay;
        await VerifyNetworkAsync(imagePath,reportPath+".network.png");
        await VerifyNestedArchivesAsync(imagePath,reportPath+".nested.png");
        await VerifyWatchingAsync(imagePath);
    }
    internal async Task VerifyFastLocationsAsync(string imagePath,string reportPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(reportPath)!,"fast-locations-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        for(var i=0;i<3000;i++)Directory.CreateDirectory(Path.Combine(root,$"folder{i:0000}"));
        var child=Path.Combine(root,"folder2300");
        for(var i=0;i<120;i++)Directory.CreateDirectory(Path.Combine(child,$"child{i:000}"));
        File.Copy(imagePath,Path.Combine(root,"page.png"));File.Copy(imagePath,Path.Combine(child,"page.png"));
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        var timings=new List<string>();
        async Task Settle(){await Dispatcher.InvokeAsync(()=>probe.UpdateLayout(),DispatcherPriority.ContextIdle);}
        async Task<LocationScrollPosition> Scroll(double fraction){await Settle();var scroll=FindScrollViewer(probe.ActiveLocationList)!;scroll.ScrollToVerticalOffset(scroll.ScrollableHeight*fraction);await Settle();return probe.CaptureLocationScroll()!;}
        void Position(LocationScrollPosition expected){
            var actual=probe.CaptureLocationScroll()!;
            var sameAnchor=actual.AnchorPath==expected.AnchorPath;
            // Inserting before a tile can move it to the next column of the same visible row.
            if(!sameAnchor&&expected.Mode==2)sameAnchor=probe.model.LocationTileRows.Any(row=>row[0].Path==actual.AnchorPath&&row.Any(entry=>entry.Path==expected.AnchorPath));
            Check(sameAnchor&&Math.Abs(actual.AnchorTop-expected.AnchorTop)<2,$"Cached position expected {expected}, actual {actual}");
        }
        try{
            probe.Show();probe.ApplyTreeVisibility(false);
            for(var mode=0;mode<3;mode++){
                probe.LocationModeBox.SelectedIndex=mode;
                var timer=Stopwatch.StartNew();await probe.OpenAsync(root);await probe.activeDisplay;await probe.locationRefresh;timings.Add($"mode {mode} open: {timer.ElapsedMilliseconds} ms");
                var selected=Path.Combine(root,"folder2200");var second=Path.Combine(root,"folder2201");
                probe.RestoreLocationSelection([selected,second],selected);var position=await Scroll(.72);
                await probe.OpenAsync(child);await probe.activeDisplay;
                var childSelected=Path.Combine(child,"child080");probe.RestoreLocationSelection([childSelected],childSelected);var childPosition=await Scroll(.45);
                var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                probe.locationRefreshBarrier=async token=>{entered.TrySetResult();await release.Task.WaitAsync(token);};
                timer.Restart();await probe.GoBackAsync();timings.Add($"mode {mode} cached Back ready (validation gated): {timer.ElapsedMilliseconds} ms");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));await Settle();
                Check(!probe.locationRefresh.IsCompleted,"Cached Back returns while full metadata validation is still blocked");
                Check(probe.model.Locations.Count==3000&&probe.model.SelectedLocationPath==selected,"Cached Back restores complete listing and primary selection immediately");
                Check(probe.Locations.SelectedItems.Count==2&&probe.model.Folder==root,"Back restores multiple selection without navigating into it");Position(position);
                var resets=0;System.Collections.Specialized.NotifyCollectionChangedEventHandler changed=(_,_)=>resets++;
                probe.model.Locations.CollectionChanged+=changed;
                release.SetResult();await probe.locationRefresh;await Settle();
                probe.model.Locations.CollectionChanged-=changed;
                Check(resets==0,"Unchanged validation does not rebuild the collection or thumbnails");Position(position);
                await probe.GoForwardAsync();await probe.activeDisplay;await probe.locationRefresh;await Settle();
                Check(probe.model.SelectedLocationPath==childSelected&&probe.model.Folder==child,"Forward restores the independent child selection");Position(childPosition);
                probe.locationRefreshBarrier=null;
            }
            await probe.OpenAsync(root);await probe.activeDisplay;await probe.locationRefresh;
            var missing=Path.Combine(root,"folder2000");probe.RestoreLocationSelection([missing],missing);var before=await Scroll(.65);
            // A pinned browser should not clear its still-current list on Back.
            await probe.OpenAsync(child,updateLocations:false);await probe.activeDisplay;
            var retained=probe.model.Locations.First();var notifications=0;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler observe=(_,_)=>notifications++;
            probe.model.Locations.CollectionChanged+=observe;
            await probe.GoBackAsync();await probe.locationRefresh;await Settle();
            probe.model.Locations.CollectionChanged-=observe;
            Check(notifications==0&&ReferenceEquals(retained,probe.model.Locations.First()),"Same-folder browser list is retained without replacement");Position(before);
            await probe.OpenAsync(child);await probe.activeDisplay;
            Directory.Delete(missing);var added=Path.Combine(root,"folder_new");Directory.CreateDirectory(added);
            await probe.GoBackAsync();await probe.locationRefresh;await Settle();
            Check(probe.model.Locations.All(entry=>entry.Path!=missing)&&probe.model.Locations.Any(entry=>entry.Path==added),"Background validation merges actual additions and removals");
            Check(probe.model.SelectedLocationPath is null&&probe.model.Folder==root,"Removed selection is cleared without opening another folder");
            var userEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var userRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.locationRefreshBarrier=async token=>{userEntered.TrySetResult();await userRelease.Task.WaitAsync(token);};
            await probe.LoadLocationsAsync(root,default,preferCached:true);await userEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Directory.CreateDirectory(Path.Combine(root,"folder_later"));
            probe.LocationScrollUserInput(probe.LocationArea,new RoutedEventArgs());var userPosition=await Scroll(.3);
            var userSelection=Path.Combine(root,"folder1000");probe.RestoreLocationSelection([userSelection],userSelection);
            userRelease.SetResult();await probe.locationRefresh;await Settle();
            Check(probe.model.SelectedLocationPath==userSelection,"Background refresh retains a selection made after cached restoration");Position(userPosition);
            var enteredOld=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseOld=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.locationRefreshBarrier=async token=>{enteredOld.TrySetResult();await releaseOld.Task.WaitAsync(token);};
            await probe.LoadLocationsAsync(root,default,preferCached:true);var oldRefresh=probe.locationRefresh;
            await enteredOld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await probe.OpenAsync(child);await probe.activeDisplay;
            releaseOld.TrySetResult();await oldRefresh;
            Check(probe.model.LocationFolder==child&&probe.model.Locations.Count==120,"Cancelled background scan cannot overwrite a newer folder");
            probe.locationRefreshBarrier=null;await probe.locationRefresh;
            var refreshes=0;probe.locationRefreshBarrier=_=>{refreshes++;return Task.CompletedTask;};
            await probe.ReloadAsync();Check(refreshes==0,"Explicit reload bypasses cached-list validation");probe.locationRefreshBarrier=null;
            var hidden=Path.Combine(root,"hidden");Directory.CreateDirectory(hidden);File.SetAttributes(hidden,File.GetAttributes(hidden)|FileAttributes.Hidden);
            probe.hideHiddenFolders=false;await probe.LoadLocationsAsync(root,default,preferCached:true);
            Check(probe.model.Locations.Any(entry=>entry.Path==hidden),"Hidden-folder setting does not reuse an incompatible snapshot");
            probe.hideHiddenFolders=true;await probe.LoadLocationsAsync(root,default,preferCached:true);await probe.locationRefresh;
            Check(probe.model.Locations.All(entry=>entry.Path!=hidden),"Hidden folder remains excluded after background validation");
            var sortEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var sortRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.locationRefreshBarrier=async token=>{sortEntered.TrySetResult();await sortRelease.Task.WaitAsync(token);};
            await probe.LoadLocationsAsync(root,default,preferCached:true);await sortEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            probe.SelectedLocationSort=3;await probe.SortLocationsAsync();sortRelease.SetResult();await probe.locationRefresh;
            Check(probe.model.Locations.SequenceEqual(FolderBrowser.SortLocations(probe.model.Locations,3))&&!probe.model.LocationsStatus.Contains("更新確認中"),"Changing sort while validating keeps the new order and clears progress status");
            probe.locationRefreshBarrier=null;
            probe.locationSnapshots.Clear();probe.locationSnapshotEntries=0;
            var sample=new LocationEntry(child,"sample",true);
            for(var i=0;i<20;i++)probe.StoreLocationSnapshot(new(root+i,true,0),[sample]);
            Check(probe.locationSnapshots.Count==LocationSnapshotLimit&&!probe.locationSnapshots.ContainsKey(new(root+0,true,0)),"Metadata snapshots evict old folders at the configured bound");
            probe.StoreLocationSnapshot(new(root+"oversized",true,0),Enumerable.Repeat(sample,LocationSnapshotEntryLimit+1).ToArray());
            Check(probe.locationSnapshotEntries<=LocationSnapshotEntryLimit&&!probe.locationSnapshots.ContainsKey(new(root+"oversized",true,0)),"Oversized snapshot is not retained");
            await File.WriteAllTextAsync(reportPath,"PASS: 3000-folder cached restoration before validation, name/detail/tile selection and scroll, unchanged-list reuse, additions/removals, cancellation, forced reload, hidden settings, bounded metadata\n"+string.Join("\n",timings));
        }
        finally{probe.locationRefreshBarrier=null;probe.Close();await probe.TemporaryCleanup;}
    }
}
