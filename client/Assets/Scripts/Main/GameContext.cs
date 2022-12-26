public class GameContext
{
    public static AssetManager assetManager;

    public static void Init()
    {
        assetManager = new();

        assetManager.Init();
    }
}