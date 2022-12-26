using UnityEngine;

/// <summary>
/// 游戏入口，但AOT，负责资源热更，加载热更Dll，使之游戏能够走到热更逻辑里
/// </summary>
public class GameEntry_AOT : MonoBehaviour
{
    private const string kGameEntryAssetPath = "Assets/";
}
