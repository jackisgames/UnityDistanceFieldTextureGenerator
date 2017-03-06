


using System;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif
using UnityEngine;
[CreateAssetMenu(menuName = "Data/DistanceFieldTexture")]
class DistanceFieldImageData : ScriptableObject
{
    public Texture2D SourceTexture;
    public Texture2D TargetTexture;
}
#if UNITY_EDITOR
[CustomEditor(typeof(DistanceFieldImageData))]
internal class DistanceFieldImageDataEditor:Editor
{
    private DistanceFieldImageData distanceFieldImageData;
    private ParseState currentParseState=ParseState.None;
    private ParseData parseData;
    private void OnEnable()
    {
        distanceFieldImageData = (DistanceFieldImageData)target;
    }

    public override void OnInspectorGUI()
    {

        base.OnInspectorGUI();
        if (currentParseState == ParseState.None)
        {
            if (distanceFieldImageData.SourceTexture == null)
            {
                EditorGUILayout.HelpBox("Insert source texture", MessageType.Error);
            }
            else if (distanceFieldImageData.TargetTexture == null)
            {
                EditorGUILayout.HelpBox("Target texture missing. Assign or Create a new one", MessageType.Warning);
                if (GUILayout.Button("Create new target texture"))
                {
                    string path = AssetDatabase.GetAssetPath(distanceFieldImageData.SourceTexture);
                    Texture2D texture = Texture2D.whiteTexture;
                    FileInfo fi = new FileInfo(path);
                    string sourceFileName = fi.Name;
                    string newFilePath = path.Remove(path.Length - sourceFileName.Length, sourceFileName.Length);
                    newFilePath = newFilePath +
                                  string.Format("{0}_distance.png", fi.Name.Replace(fi.Extension, string.Empty));

                    File.WriteAllBytes(newFilePath, texture.EncodeToPNG());
                    AssetDatabase.Refresh();

                    distanceFieldImageData.TargetTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(newFilePath);
                    EditorUtility.SetDirty(target);
                }
            }
            else
            {
                if (GUILayout.Button("Generate Distance Field Texture"))
                {
                    parseData = new ParseData();
                    parseData.SourcePath = AssetDatabase.GetAssetPath(distanceFieldImageData.SourceTexture);
                    parseData.TargetPath = AssetDatabase.GetAssetPath(distanceFieldImageData.TargetTexture);
                    parseData.SourceImporter = (TextureImporter)AssetImporter.GetAtPath(parseData.SourcePath);
                    parseData.TargetImporter = (TextureImporter)AssetImporter.GetAtPath(parseData.TargetPath);
                    parseData.SourceTexture = distanceFieldImageData.SourceTexture;
                    parseData.TargetTexture = distanceFieldImageData.TargetTexture;
                    parseData.progressTotal = parseData.SourceTexture.width * parseData.SourceTexture.height*3;
                    currentParseState = ParseState.PreparingTexture;

                }
            }
        }
        else
        {
            EditorUtility.DisplayProgressBar("Please Wait...",currentParseState.ToString(), parseData.progressCurrent /parseData.progressTotal);
            switch (currentParseState)
            {
                case ParseState.PreparingTexture:
                    PreparingTextures();
                    break;
                case ParseState.PreparingGrids:
                    PrepareGrids(parseData.SourceTexture, parseData.grid1, parseData.grid2);
                    currentParseState = ParseState.CalculatingDistance;
                    break;
                case ParseState.CalculatingDistance:
                    //calc grid 1&2
                    GenerateSDF(parseData.grid1);
                    GenerateSDF(parseData.grid2);
                    currentParseState = ParseState.WritingPixels;
                    break;
                case ParseState.WritingPixels:
                    GenerateDistanceImage();
                    currentParseState = ParseState.Saving;
                    break;
                case ParseState.Saving:
                    SavingGeneratedImage();
                    currentParseState = ParseState.RevertingAllSetting;
                    break;
                case ParseState.RevertingAllSetting:
                    RevertingTextures();

                    break;
            }
        }
    }

    private void PreparingTextures()
    {

        
        //prepare texture
        
        if (parseData.SourceImporter.isReadable&& parseData.TargetImporter.isReadable)
        {
            parseData.grid1.Init(parseData.SourceTexture.width, parseData.SourceTexture.height);
            parseData.grid2.Init(parseData.SourceTexture.width, parseData.SourceTexture.height);
            currentParseState =ParseState.PreparingGrids;
        }
        else
        {
            parseData.SourceImporter.isReadable = true;
            parseData.TargetImporter.isReadable = true;
            parseData.SourceImporter.SaveAndReimport();
            parseData.TargetImporter.SaveAndReimport();
        }

        
    }
    private void RevertingTextures()
    {


        //prepare texture

        if (!parseData.SourceImporter.isReadable && !parseData.TargetImporter.isReadable)
        {
            currentParseState = ParseState.None;
            EditorUtility.ClearProgressBar();
        }
        else
        {
            parseData.SourceImporter.isReadable = false;
            parseData.TargetImporter.isReadable = false;
            parseData.TargetImporter.textureCompression=TextureImporterCompression.Uncompressed;
            parseData.SourceImporter.SaveAndReimport();
            parseData.TargetImporter.SaveAndReimport();
        }



    }
    private void PrepareGrids(Texture2D source,Grid grid1,Grid grid2)
    {
        Vector2 inside=new Vector2();
        Vector2 empty=new Vector2(9999,9999);
        for (int x = 0; x < source.width; x++)
        {
            for (int y = 0; y < source.height; y++)
            {
                Color c = source.GetPixel(x, y);
                float grayScale = c.grayscale;
                if (grayScale < .5f)
                {
                    Put(grid1, x, y, inside);
                    Put(grid2, x, y, empty);
                }
                else
                {
                    Put(grid2, x, y, inside);
                    Put(grid1, x, y, empty);
                }
            }
        }
    }

    private void GenerateDistanceImage()
    {
        Texture2D targetTexture = new Texture2D(parseData.SourceTexture.width, parseData.SourceTexture.height);
        float spread = 128;//Mathf.Max(targetTexture.width, targetTexture.height);
        for (int x = 0; x < targetTexture.width; x++)
        {
            for (int y = 0; y < targetTexture.height; y++)
            {
                Vector2 pA = Get(parseData.grid1, x, y);
                Vector2 pB = Get(parseData.grid2, x, y);
                float dist1 = pA.magnitude;
                float dist2 = pB.magnitude;
                float dist = .5f+(dist1 - dist2)/ spread;
                Color c=new Color(dist,dist,dist,1);
                targetTexture.SetPixel(x,y,c);
                parseData.progressCurrent++;
            }
        }
        parseData.TargetTexture = targetTexture;
    }
    private void SavingGeneratedImage()
    {
        File.WriteAllBytes(parseData.TargetPath,parseData.TargetTexture.EncodeToPNG());
    }
    private void GenerateSDF(Grid g)
    {
        //pass 0
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                Vector2 p = Get(g, x, y);
                 p = Compare(g, p, x, y, -1, 0);
                 p = Compare(g, p, x, y, 0, -1);
                 p = Compare(g,  p, x, y, -1, -1);
                 p = Compare(g,  p, x, y, 1, -1);

               
                Put(g, x, y, p);
                parseData.progressCurrent++;
            }
            for (int x = g.Width - 1; x >= 0; x--)
            {
                Vector2 p = Get(g, x, y);
                p = Compare(g, p, x, y, 1, 0);
                Put(g, x, y, p);
            }
        }

        //pass 1
        for (int y = g.Height - 1; y >= 0; y--)
        {
            for (int x = g.Width - 1; x >= 0; x--)
            {
                Vector2 p = Get(g, x, y);
                p=Compare(g, p, x, y, 1, 0);
                p = Compare(g, p, x, y, 0, 1);
                p = Compare(g, p, x, y, -1, 1);
                p = Compare(g, p, x, y, 1, 1);

                

                Put(g, x, y, p);
                parseData.progressCurrent++;
            }

            for (int x = 0; x < g.Width; x++)
            {
                Vector2 p = Get(g, x, y);
                p = Compare(g, p, x, y, -1, 0);
                Put(g, x, y, p);
            }
        }
    }

    private Vector2 Compare(Grid g, Vector2 p,int x,int y,int offsetx,int offsety)
    {
        Vector2 other = Get(g, x + offsetx, y + offsety);
        other.x += offsetx;
        other.y += offsety;

        if (other.sqrMagnitude < p.sqrMagnitude)
        {
            return other;
        }
        return p;

    }
    private void Put(Grid g, int x, int y, Vector2 value)
    {
        /*Point p = Get(g, x, y);
        p.Dx = value.Dx;
        p.Dy = value.Dy;*/
        g.SetPoint(x,y,value);
    }
    
    private Vector2 Get(Grid g,int x,int y)
    {
        return g.GetPoint(x, y);
    }

    public override bool RequiresConstantRepaint()
    {
        return currentParseState != ParseState.None;
    }
}
internal class ParseData
{
    public string SourcePath;
    public string TargetPath;
    public TextureImporter SourceImporter;
    public TextureImporter TargetImporter;
    public Texture2D SourceTexture;
    public Texture2D TargetTexture;

    public Grid grid1 = new Grid();
    public Grid grid2 = new Grid();

    public float progressTotal;
    public float progressCurrent = 0;
}
/*internal class Point
{
    public float Dx = 9999f;
    public float Dy=9999f;
    
    public float DistSq()
    {
        return Dx * Dx + Dy * Dy;
    }
}*/
internal class Grid
{
    public int Width;
    public int Height;
    private Vector2[] grids;
    public void Init(int width,int height)
    {
        Width = width;
        Height = height;
        grids=new Vector2[width*height];
        for(int i=0;i<grids.Length;i++)
            grids[i]=new Vector2();
    }
    private int GetPosition(int x,int y)
    {
        int result = (x % Width) + y * Width;
        if (result < 0)
            return -1;
        else if (result >= grids.Length)
            return - 1;
        return result;
    }
    public Vector2 GetPoint(int x,int y)
    {
        int pos = GetPosition(x, y);
        if(pos<0)
            return new Vector2(10000, 10000);
        return grids[pos];
    }
    public void SetPoint(int x, int y, Vector2 point)
    {
        int pos = GetPosition(x, y);
        if (pos >= 0)
        grids[pos]=point;
    }
}
internal enum ParseState
{
    None,
    PreparingTexture,
    PreparingGrids,
    CalculatingDistance,
    WritingPixels,
    Saving,
    RevertingAllSetting
}
#endif
