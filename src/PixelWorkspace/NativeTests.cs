using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace PixelWorkspace;
internal static class NativeTests
{
 internal static string Run(string fixturesPath)
 {
  using var json=JsonDocument.Parse(File.ReadAllText(fixturesPath));int cases=0;
  foreach(var item in json.RootElement.EnumerateArray())
  {
   int w=item.GetProperty("width").GetInt32(),h=item.GetProperty("height").GetInt32();
   var expected=item.GetProperty("indices").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
   if(!expected.SequenceEqual(ObfuscationEngine.Curve(w,h)))throw new Exception($"Curve mismatch {w}x{h}");
   byte[] original=new byte[w*h*4];new Random(w*100+h).NextBytes(original);
   var encrypted=ObfuscationEngine.Transform(original,w,h,false);var restored=ObfuscationEngine.Transform(encrypted,w,h,true);
   if(!restored.SequenceEqual(original))throw new Exception($"Roundtrip mismatch {w}x{h}");
   int shift=item.GetProperty("offset").GetInt32();var reference=new byte[original.Length];for(int i=0;i<w*h;i++)Buffer.BlockCopy(original,expected[i]*4,reference,expected[(i+shift)%(w*h)]*4,4);
   if(!reference.SequenceEqual(encrypted))throw new Exception("Encryption direction mismatch");cases++;
  }
  byte[] pixels=[10,20,30,255,50,60,70,128,90,100,110,0,120,130,140,255];
  var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8);
  byte[] png=ImageFiles.Encode(bitmap,true);var decoded=ImageFiles.Decode(png);byte[] output=new byte[16];decoded.CopyPixels(output,8,0);
  if(!pixels.SequenceEqual(output))throw new Exception("PNG transparency roundtrip mismatch");
  int chunks=0;for(int offset=8;offset+12<=png.Length;){int length=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset,4));string type=System.Text.Encoding.ASCII.GetString(png,offset+4,4);if(new[]{"eXIf","tEXt","iTXt","zTXt"}.Contains(type))throw new Exception("Clean PNG contains metadata");offset+=length+12;chunks++;}
  var tagged=ImageFiles.InjectPngText(png,new Dictionary<string,string>{{"Comment","fixture-secret"},{"GPS","test-location"}});
  if(ImageFiles.ReadPngText(tagged).Count!=2)throw new Exception("PNG metadata fixture injection failed");
  var clean=ImageFiles.Encode(ImageFiles.Decode(tagged),true);
  if(ImageFiles.ReadPngText(clean).Count!=0)throw new Exception("Metadata removal failed");
  string editorTests=EditorEngine.RunSelfTests();
  return editorTests+"\n"+$"PASS: tagged PNG metadata removal; {cases} JavaScript curve compatibility cases; {cases} byte-exact permutation references; {cases} inverse roundtrips; PNG alpha roundtrip; clean PNG metadata chunks ({chunks} structural chunks).";
 }
}
