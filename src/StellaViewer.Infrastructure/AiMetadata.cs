using System.Text;
using System.Text.Json;
namespace StellaViewer.Infrastructure;

public sealed record AiMetadataField(string Name,string Value);
public sealed record AiMetadataReport(IReadOnlyList<AiMetadataField> Fields,PngTextResult Source)
{
    public string DisplayText()
    {
        const int maximum=65536;var text=new StringBuilder();
        void Add(string label,string value){if(text.Length>=maximum)return;var content=$"{label}\n{value}\n\n";var remaining=maximum-text.Length;text.Append(content.AsSpan(0,Math.Min(content.Length,remaining)));}
        if(Source.Notice is not null)Add("読み取りについて",Source.Notice);
        if(Fields.Count==0)Add("AI生成情報","対応するAIメタデータなし");else foreach(var field in Fields)Add(field.Name,field.Value);
        if(Source.Entries.Count>0){Add("元のPNGテキスト","以下は画像内に保存されている情報です。");foreach(var entry in Source.Entries)Add(entry.Key,entry.Text);}
        if(text.Length>=maximum)text.Append("\n\n長い情報は表示上限（65,536文字）で省略しています。");
        return text.ToString().TrimEnd();
    }
}

public static class AiMetadata
{
    public static AiMetadataReport Parse(PngTextResult source)
    {
        var fields=new List<AiMetadataField>();var tags=source.Entries.GroupBy(item=>item.Key,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.First().Text,StringComparer.OrdinalIgnoreCase);
        void Add(string name,string? value){if(fields.Count<128&&!string.IsNullOrWhiteSpace(value))fields.Add(new(name,value.Length<=16384?value:value[..16384]+"\n（長い値は省略。元のPNGテキストを参照してください。）"));}
        string? Tag(string key)=>tags.GetValueOrDefault(key);
        if(Tag("parameters") is{} parameters){
            Add("生成情報の形式","AUTOMATIC1111互換");
            var normalized=parameters.Replace("\r\n","\n");var line=normalized.LastIndexOf("\nSteps:",StringComparison.Ordinal);if(normalized.StartsWith("Steps:",StringComparison.Ordinal))line=-1;
            var start=line>=0?line+1:normalized.StartsWith("Steps:",StringComparison.Ordinal)?0:normalized.Length;
            var prompt=normalized[..start].TrimEnd();var negative=prompt.LastIndexOf("\nNegative prompt:",StringComparison.Ordinal);
            if(prompt.StartsWith("Negative prompt:",StringComparison.Ordinal))negative=0;
            if(negative>=0){var marker=negative==0?0:negative+1;Add("プロンプト",prompt[..negative].TrimEnd());Add("ネガティブプロンプト",prompt[(marker+"Negative prompt:".Length)..].TrimStart());}else Add("プロンプト",prompt);
            foreach(var item in SplitParameters(normalized[start..])){
                var separator=item.IndexOf(':');if(separator<1)continue;var key=item[..separator].Trim();var value=item[(separator+1)..].Trim();
                Add(key switch{"Model"=>"モデル","Model hash"=>"モデルハッシュ","Steps"=>"Steps","Sampler"=>"Sampler","CFG scale"=>"CFG","Seed"=>"Seed",_=>key},value);
            }
        }
        else if(Tag("Software") is{} software&&software.Contains("NovelAI",StringComparison.OrdinalIgnoreCase)){
            Add("生成ツール",software);Add("モデル／Source",Tag("Source"));Add("プロンプト",Tag("Description"));
            if(Tag("Comment") is{} comment)WithJson(comment,root=>{
                if(root.ValueKind!=JsonValueKind.Object)return;
                foreach(var pair in new[]{("prompt","プロンプト（Comment）"),("uc","ネガティブプロンプト"),("sampler","Sampler"),("steps","Steps"),("scale","CFG"),("seed","Seed"),("model","モデル")})Add(pair.Item2,Scalar(root,pair.Item1));
            });
        }
        else if(Tag("prompt") is{} graph){
            WithJson(graph,root=>{
                if(root.ValueKind!=JsonValueKind.Object)return;
                var nodes=root.EnumerateObject().Take(513).ToArray();if(nodes.Length>512)return;
                if(!nodes.Any(node=>node.Value.ValueKind==JsonValueKind.Object&&node.Value.TryGetProperty("class_type",out _)))return;
                Add("生成情報の形式","ComfyUI prompt");
                var samplers=0;foreach(var node in nodes){
                    if(Scalar(node.Value,"class_type") is not("KSampler" or "KSamplerAdvanced"))continue;
                    if(++samplers>16)break;
                    if(!node.Value.TryGetProperty("inputs",out var inputs)||inputs.ValueKind!=JsonValueKind.Object)continue;
                    var prefix=$"ノード {node.Name} / ";
                    Add(prefix+"プロンプト",Resolve(root,inputs,"positive","text",new()));Add(prefix+"ネガティブプロンプト",Resolve(root,inputs,"negative","text",new()));
                    Add(prefix+"モデル",Resolve(root,inputs,"model","ckpt_name",new())??Resolve(root,inputs,"model","unet_name",new()));
                    foreach(var key in new[]{"steps","cfg","seed","noise_seed","sampler_name","scheduler"})Add(prefix+(key switch{"steps"=>"Steps","cfg"=>"CFG","seed" or "noise_seed"=>"Seed","sampler_name"=>"Sampler",_=>key}),Scalar(inputs,key));
                }
            });
        }
        if(fields.Count==0&&Tag("workflow") is{} workflow)WithJson(workflow,root=>{if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("nodes",out var nodes)&&nodes.ValueKind==JsonValueKind.Array)Add("生成情報の形式","ComfyUI workflow（詳細は元のテキスト）");});
        return new(fields,source);
    }
    private static string? Scalar(JsonElement root,string name)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(name,out var value)&&value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False?value.ValueKind==JsonValueKind.String?value.GetString():value.GetRawText():null;
    private static void WithJson(string text,Action<JsonElement> action){try{using var json=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=32});action(json.RootElement);}catch(JsonException){ }}
    private static string? Resolve(JsonElement graph,JsonElement inputs,string link,string field,HashSet<string> seen)
    {
        if(!inputs.TryGetProperty(link,out var connection)||connection.ValueKind!=JsonValueKind.Array||connection.GetArrayLength()!=2||connection[0].ValueKind!=JsonValueKind.String)return null;
        var id=connection[0].GetString()!;if(seen.Count>=32||!seen.Add(id)||!graph.TryGetProperty(id,out var node)||node.ValueKind!=JsonValueKind.Object||!node.TryGetProperty("inputs",out var target)||target.ValueKind!=JsonValueKind.Object)return null;
        // Follow only known pass-through nodes; custom graph logic remains visible as raw JSON.
        var kind=Scalar(node,"class_type");
        if(field=="text")return kind=="CLIPTextEncode"?Scalar(target,field):null;
        if(kind is "CheckpointLoaderSimple" or "UNETLoader")return Scalar(target,field);
        return kind is "LoraLoader" or "LoraLoaderModelOnly"?Resolve(graph,target,"model",field,seen):null;
    }
    private static IEnumerable<string> SplitParameters(string text)
    {
        var start=0;var quoted=false;var escaped=false;var depth=0;
        for(var i=0;i<text.Length;i++){
            var c=text[i];if(escaped){escaped=false;continue;}if(quoted&&c=='\\'){escaped=true;continue;}if(c=='"'){quoted=!quoted;continue;}
            if(quoted)continue;if(c is '{' or '[' or '(')depth++;else if(c is '}' or ']' or ')')depth=Math.Max(0,depth-1);
            else if(c==','&&depth==0){yield return text[start..i];start=i+1;}
        }
        yield return text[start..];
    }
}
