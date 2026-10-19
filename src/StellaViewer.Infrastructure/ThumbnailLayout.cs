namespace StellaViewer.Infrastructure;

public sealed record ThumbnailLayout(double DetailWidth,double DetailHeight,double DetailRowHeight,
    double TileWidth,double TileHeight,double TileImageWidth,double TileImageHeight,
    double DetailIconSize,double TileIconSize,int DecodeEdge)
{
    public static int Normalize(int size)=>size is >=0 and <=3?size:1;
    public static ThumbnailLayout Create(int size,bool locations,double availableWidth=double.PositiveInfinity)
    {
        var scale=Normalize(size) switch{0=>.75,2=>1.5,3=>2,_=>1};
        if(!double.IsFinite(availableWidth)||availableWidth<=24)availableWidth=double.PositiveInfinity;
        var detailWidth=Math.Min((locations?56:68)*scale,Math.Max(24,(availableWidth-24)*.6));
        var detailHeight=detailWidth*(locations?1:64d/68);
        var tileWidth=Math.Min(96*scale+16,Math.Max(48,availableWidth-24));
        var imageWidth=tileWidth-16;var imageHeight=imageWidth*78/96;
        return new(detailWidth,detailHeight,Math.Max(locations?68:48,detailHeight+(locations?12:16)),
            tileWidth,imageHeight+46,imageWidth,imageHeight,Math.Min(32*scale,detailWidth),
            Math.Max(12,Math.Min(36*scale,imageHeight-20)),(int)Math.Ceiling(Math.Max(detailWidth,imageWidth)));
    }
}
