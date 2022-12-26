using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class AssetManager
{
    public void Init()
    {
    }

    private Dictionary<Object, List<AsyncOperationHandle>> asset_handleList      = new(); // 追踪每一个上层业务产生的 Handle，并在合适的时机释放掉
    
    /// Addressables 在某个 AssetBundle 的所有 Asset 的引用计数都变成0的时候，会自动调用 AssetBundle.Unload(true) 卸载掉对应的 AssetBundle
    /// 不过，我们有时候更希望更主动地控制 AssetBundle.Unload() 的时机。直到我们显示调用 AssetBundle.Unload()，对应的 Asset 都会一直缓存在内存中
    /// 这个问题的解决方案是，为我们希望缓存的资产维护一份额外的引用计数，这份引用计数会通过 UncacheAsset 进行清理
    private Dictionary<string, LoadRequest>                assetPath_loadRequest = new(); 

    public T LoadAsset<T>(string assetPath) where T : Object
    {
        var handle = Addressables.LoadAssetAsync<T>(assetPath);
        handle.WaitForCompletion();
        TryRecordHandle(handle);
        return handle.Result;
    }

    public void LoadAssetAsync<T>(string assetPath, Action<T> callback) where T : Object
    {
        var handle = Addressables.LoadAssetAsync<T>(assetPath);
        handle.Completed += newHandle => { callback?.Invoke(TryRecordHandle(newHandle) ? newHandle.Result : null); };
    }

    public GameObject InstantiateGameObject(string assetPath)
    {
        AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(assetPath);
        handle.WaitForCompletion();
        TryRecordHandle(handle);
        return handle.Result;
    }

    public void InstantiateGameObjectAsync(string assetPath, Action<GameObject> callback)
    {
        var handle = Addressables.InstantiateAsync(assetPath);
        handle.Completed += newHandle => { callback?.Invoke(TryRecordHandle(newHandle) ? newHandle.Result : null); };
    }

    public void LoadAssetsAsync(string[] assetPaths, Action<Object> completePerObjectCallback, Action<IList<Object>> completedCallback)
    {
        var handle = Addressables.LoadAssetsAsync((IEnumerable)assetPaths, completePerObjectCallback, Addressables.MergeMode.Union);
        handle.Completed += newHandle => { completedCallback?.Invoke(TryRecordHandle(newHandle) ? newHandle.Result : null); };
    }

    public void LoadSceneAsync(string assetPath, LoadSceneMode loadSceneMode = LoadSceneMode.Single, Action<bool> callback = null)
    {
        var handle = Addressables.LoadSceneAsync(assetPath, loadSceneMode);
        handle.Completed += newHandle =>
        {
            if (newHandle.IsValid() && newHandle.IsDone && newHandle.Status == AsyncOperationStatus.Succeeded)
            {
                callback?.Invoke(true);
            }
            else
            {
                callback?.Invoke(false);
            }
        };
    }

    public void UnloadSceneAsync(SceneInstance sceneInstance, Action<bool> callback)
    {
        var handle = Addressables.UnloadSceneAsync(sceneInstance);
        handle.Completed += newHandle =>
        {
            if (newHandle.IsDone && newHandle.IsValid() && newHandle.Status == AsyncOperationStatus.Succeeded)
            {
                callback?.Invoke(true);
            }
            else
            {
                callback?.Invoke(false);
            }
        };
    }

    public void ReleaseObject<T>(T obj) where T : Object
    {
        if (typeof(T) == typeof(GameObject))
        {
            Object.Destroy(obj);
        }

        Addressables.Release(obj);
    }

    public void UnloadAllAsset()
    {
        foreach (var handleList in asset_handleList.Values)
        {
            foreach (var handle in handleList)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        asset_handleList.Clear();

        foreach (var keyValuePair in assetPath_loadRequest)
        {
            var loadRequest = keyValuePair.Value;
            if (loadRequest.handle.IsValid())
            {
                loadRequest.isDead = true;
                Addressables.Release(loadRequest.handle);
            }
        }

        assetPath_loadRequest.Clear();

        Resources.UnloadUnusedAssets();
    }

    /// <summary>
    /// 参考 assetPath_loadRequest
    /// </summary>
    /// <param name="assetPath">要缓存的资产的路径</param>
    /// <param name="callback">由于我们首先要加载出该资产，才谈得上引用计数的增加，所以该接口是一个异步接口，通过回调来返回执行的结果</param>
    public void CacheAsset(string assetPath, Action<bool> callback = null)
    {
        CheckAndUpdateAssetExtraHandle(assetPath, callback);
    }

    /// <summary>
    /// 参考 assetPath_loadRequest
    /// </summary>
    public void UncacheAsset(string assetPath)
    {
        if (assetPath_loadRequest.TryGetValue(assetPath, out var loadRequest))
        {
            loadRequest.isDead = true;
            assetPath_loadRequest.Remove(assetPath);
        }
    }

    #region private

    /// <summary>
    /// 检查并更新资产的额外引用计数，参考 assetPath_loadRequest
    /// </summary>
    private void CheckAndUpdateAssetExtraHandle(string assetPath, Action<bool> callback = null)
    {
        if (assetPath_loadRequest.ContainsKey(assetPath))
        {
            callback?.Invoke(true);
            return;
        }

        var loadRequest = new LoadRequest { isDead = false };
        assetPath_loadRequest.Add(assetPath, loadRequest);

        var handle = Addressables.LoadAssetAsync<Object>(assetPath);
        handle.Completed += newHandle =>
        {
            if (!loadRequest.isDead && newHandle.IsDone && newHandle.IsValid() && newHandle.Status == AsyncOperationStatus.Succeeded)
            {
                loadRequest.handle = handle;
                callback?.Invoke(true);
            }
            else
            {
                if (newHandle.IsValid())
                {
                    Addressables.Release(newHandle);
                }

                callback?.Invoke(false);
            }
        };
    }

    /// <summary>
    /// 记录新生成的 handle。参考 asset_handleList
    /// </summary>
    private bool TryRecordHandle(AsyncOperationHandle handle)
    {
        if (!handle.IsDone || !handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded) return false;
        
        if (handle.Result is IList<Object>)
        {
            var assetList = handle.Result as IList<Object>;
            foreach (var asset in assetList)
            {
                if (!asset_handleList.ContainsKey(asset))
                {
                    asset_handleList[asset] = new();
                }

                asset_handleList[asset].Add(handle);
            }
        }
        else
        {
            var asset = handle.Result as Object;
            if (!asset_handleList.ContainsKey(asset))
            {
                asset_handleList[asset] = new();
            }

            asset_handleList[asset].Add(handle);
        }

        return true;
    }

    private struct LoadRequest
    {
        public bool                 isDead;
        public AsyncOperationHandle handle;
    }

    #endregion
}