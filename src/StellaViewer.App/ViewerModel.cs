using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;
public sealed class ViewerModel : INotifyPropertyChanged
{
    private bool allowNetwork;
    private ArchiveSession? archiveSession;
    public ArchiveSession? ArchiveSession { get => archiveSession; set { archiveSession = value; Changed(); } }
    private ArchiveSession? locationArchiveSession;
    public ArchiveSession? LocationArchiveSession { get => locationArchiveSession; set { locationArchiveSession = value; Changed(); } }
    private string locationFolder = "";
    public string LocationFolder { get => locationFolder; set { locationFolder = value; Changed(); Changed(nameof(CanRegisterDirectoryShortcut)); } }
    public bool CanRegisterDirectoryShortcut=>LocationFolder.Length>0&&!BrowsePath.IsVirtual(LocationFolder);
    private PdfSession? pdfSession;
    public PdfSession? PdfSession { get => pdfSession; set { pdfSession = value; Changed(); } }
    private ArchiveTemporaryStore? temporaryStore;
    public ArchiveTemporaryStore? TemporaryStore { get => temporaryStore; set { temporaryStore = value; Changed(); } }
    public bool AllowNetwork { get => allowNetwork; set { allowNetwork = value; Changed(); } }
    private bool canGoBack;
    public bool CanGoBack { get => canGoBack; set { canGoBack = value; Changed(); } }
    private bool canGoForward;
    public bool CanGoForward { get => canGoForward; set { canGoForward = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private LiveEntryCollection<FileEntry> entries = [];
    public LiveEntryCollection<FileEntry> Entries { get => entries; set { entries = value; Changed(); } }
    public LiveEntryCollection<FileEntry[]> TileRows { get; } = [];
    public LiveEntryCollection<LocationEntry> Locations { get; } = [];
    public LiveEntryCollection<LocationEntry[]> LocationTileRows { get; } = [];
    public LiveEntryCollection<FavoriteItem> Favorites { get; } = [];
    public LiveEntryCollection<DirectoryShortcutItem> DirectoryShortcuts { get; } = [];
    public LiveEntryCollection<RecentLocation> RecentLocations { get; } = [];
    private int imageThumbnailSize=1,locationThumbnailSize=1;
    private double imageThumbnailWidth=double.PositiveInfinity,locationThumbnailWidth=double.PositiveInfinity;
    private double uiFontFactor = 1;
    public double UiFontFactor { get => uiFontFactor; set { uiFontFactor = value; UpdateImageThumbnailWidth(imageThumbnailWidth); UpdateLocationThumbnailWidth(locationThumbnailWidth); Changed(nameof(ListNameFontSize)); Changed(nameof(ListTileFontSize)); Changed(nameof(ListInfoFontSize)); Changed(nameof(ListSmallInfoFontSize)); Changed(nameof(ListCaptionHeight)); } }
    public double ListNameFontSize => 14 * uiFontFactor;
    public double ListTileFontSize => 12 * uiFontFactor;
    public double ListInfoFontSize => 11 * uiFontFactor;
    public double ListSmallInfoFontSize => 10 * uiFontFactor;
    public double ListCaptionHeight => 32 * Math.Max(1, uiFontFactor);
    private ThumbnailLayout SizedThumbnails(int size, bool locations, double width)
    {
        var layout = ThumbnailLayout.Create(size, locations, width); var extra = Math.Max(0, uiFontFactor - 1);
        return layout with { TileHeight = layout.TileHeight + 32 * extra, DetailRowHeight = Math.Max(layout.DetailRowHeight, (locations ? 68 : 48) * uiFontFactor) };
    }
    public ThumbnailLayout ImageThumbnails { get; private set; }=ThumbnailLayout.Create(1,false);
    public ThumbnailLayout LocationThumbnails { get; private set; }=ThumbnailLayout.Create(1,true);
    public int ImageThumbnailSize { get=>imageThumbnailSize;set{imageThumbnailSize=ThumbnailLayout.Normalize(value);UpdateImageThumbnailWidth(imageThumbnailWidth);} }
    public int LocationThumbnailSize { get=>locationThumbnailSize;set{locationThumbnailSize=ThumbnailLayout.Normalize(value);UpdateLocationThumbnailWidth(locationThumbnailWidth);} }
    public void UpdateImageThumbnailWidth(double width){imageThumbnailWidth=width;var layout=SizedThumbnails(imageThumbnailSize,false,width);if(layout!=ImageThumbnails){ImageThumbnails=layout;Changed(nameof(ImageThumbnails));}}
    public void UpdateLocationThumbnailWidth(double width){locationThumbnailWidth=width;var layout=SizedThumbnails(locationThumbnailSize,true,width);if(layout!=LocationThumbnails){LocationThumbnails=layout;Changed(nameof(LocationThumbnails));}}
    private IReadOnlySet<string> selectedImagePaths = new HashSet<string>(), selectedLocationPaths = new HashSet<string>();
    public IReadOnlySet<string> SelectedImagePaths { get => selectedImagePaths; set { selectedImagePaths = value; Changed(); } }
    public IReadOnlySet<string> SelectedLocationPaths { get => selectedLocationPaths; set { selectedLocationPaths = value; Changed(); } }
    private string? selectedLocationPath;
    public string? SelectedLocationPath { get => selectedLocationPath; set { selectedLocationPath = value; Changed(); } }
    private string currentAddress = "";
    public string CurrentAddress { get => currentAddress; set { currentAddress = value; Changed(); } }
    private string locationsStatus = "フォルダ・書庫";
    public string LocationsStatus { get => locationsStatus; set { locationsStatus = value; Changed(); } }
    private string? selectedPath;
    public string? SelectedPath { get => selectedPath; set { selectedPath = value; Changed(); } }
    private string folder = "";
    public string Folder { get => folder; set { folder = value; Changed(); } }
    private string status = "フォルダを開いてください";
    public string Status { get => status; set { status = value; Changed(); } }
    private string listingStatus = "画像一覧";
    public string ListingStatus { get => listingStatus; set { listingStatus = value; Changed(); } }
    private string message = "フォルダまたは画像をドロップして開けます";
    public string Message { get => message; set { message = value; Changed(); } }
    private ImageSource? image;
    public ImageSource? Image { get => image; set { image = value; Changed(); Changed(nameof(HasImage)); } }
    public bool HasImage => Image is not null;
    private string details = "画像を選択してください";
    public string Details { get => details; set { details = value; Changed(); } }
    private string metadataText="画像を選択してください。";
    public string MetadataText{get=>metadataText;set{metadataText=value;Changed();}}
}
