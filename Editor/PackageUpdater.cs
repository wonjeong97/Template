using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
#if HULIACDEV_NUGETFORUNITY
using System;
using NugetForUnity;
using NugetForUnity.Configuration;
using NugetForUnity.Models;
#endif

namespace HuliacDev.Editor
{
    /// <summary>
    /// 이 템플릿 스택이 직접 의존하는 패키지만 골라 최신화하는 에디터 유틸리티.
    /// 예전에는 설치된 UPM 패키지 전부를 대상으로 삼아, 프로젝트별로 버전을 고정해둔
    /// 패키지(Addressables, Input System, URP 등)까지 의도치 않게 끌어올리는 문제가 있었음.
    /// 그래서 <see cref="TargetPackageNames"/>에 명시된 패키지(이 스택이 실제로 관리하는 것들과
    /// 템플릿 자신)만 대상으로 좁힘.
    /// <para>
    /// Git 패키지는 동일 URL로 재추가해 최신 커밋으로 갱신하고, 레지스트리 패키지는
    /// 현재 에디터와 호환되는 최신 버전(versions.latestCompatible)으로만 올린다.
    /// </para>
    /// <para>
    /// UPM 패키지를 올린 뒤 NuGetForUnity가 설치된 프로젝트면 NuGet 창의 Updates 탭에 뜨는 패키지(정식 버전)도
    /// 모두 올림. ZLogger·R3처럼 UPM 패키지는 Unity 연동 레이어이고 실제 구현 DLL은 NuGet에 있어, UPM 쪽만 올리면
    /// 두 버전이 어긋나기 때문.
    /// </para>
    /// </summary>
    public static class PackageUpdater
    {
        /// <summary>
        /// 업데이트 대상 패키지 이름(manifest.json의 키) 목록. 여기 없는 패키지는 설치돼 있어도
        /// 건드리지 않음. 새 스택 패키지가 추가되면 이 목록에도 추가할 것.
        /// </summary>
        static readonly HashSet<string> TargetPackageNames = new HashSet<string>
        {
            "com.coplaydev.unity-mcp",          // MCP for Unity
            "com.cysharp.messagepipe",          // MessagePipe
            "com.cysharp.messagepipe.vcontainer", // MessagePipe.VContainer
            "com.cysharp.r3",                   // R3
            "com.cysharp.unitask",              // UniTask
            "com.cysharp.zlogger",              // ZLogger(.Unity)
            "jp.hadashikick.vcontainer",        // VContainer
            "com.github-glitchenzo.nugetforunity", // NugetForUnity
            "com.cysharp.zstring",              // ZString
            "com.huliacdev.template",           // 현재 템플릿
        };

        /// <summary>
        /// UPM 업데이트 뒤 NuGet 업데이트가 남아 있음을 도메인 리로드 너머로 전하는 SessionState 키.
        /// UPM 패키지가 바뀌면 다시 컴파일되며 정적 상태가 사라지므로, 완료 콜백보다 리로드가 먼저 오면 이 값으로 이어서 실행함.
        /// </summary>
        const string PendingNugetUpdateKey = "HuliacDev.PackageUpdater.PendingNugetUpdate";

        static ListRequest _listRequest;
        static AddAndRemoveRequest _addRequest;
        static HashSet<string> _requestedNames;

        [MenuItem("Tools/Update Stack Packages")]
        static void Run()
        {
            // offlineMode: false → 레지스트리에서 최신 버전 정보 조회
            _listRequest = Client.List(offlineMode: false, includeIndirectDependencies: false);
            EditorApplication.update += WaitForList;
            EditorUtility.DisplayProgressBar("Package Updater", "패키지 목록 조회 중...", 0f);
        }

        /// <summary>
        /// UPM 업데이트 중 도메인 리로드가 먼저 와서 NuGet 업데이트를 하지 못했으면, 리로드 뒤 이어서 실행함.
        /// </summary>
        [InitializeOnLoadMethod]
        static void ResumePendingNugetUpdate()
        {
            if (!SessionState.GetBool(PendingNugetUpdateKey, false)) return;

            EditorApplication.delayCall += () =>
            {
                Debug.Log("[PackageUpdater] Recompiled after the UPM update; continuing with the remaining NuGet package updates.");
                UpdateNugetPackages();
                EditorUtility.ClearProgressBar();
            };
        }

        static void WaitForList()
        {
            if (!_listRequest.IsCompleted) return;
            EditorApplication.update -= WaitForList;

            if (_listRequest.Status != StatusCode.Success)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[PackageUpdater] Failed to list packages: {_listRequest.Error.message}");
                return;
            }

            List<string> packagesToAdd = new List<string>();
            _requestedNames = new HashSet<string>();

            foreach (UnityEditor.PackageManager.PackageInfo pkg in _listRequest.Result)
            {
                if (!TargetPackageNames.Contains(pkg.name))
                    continue;

                if (pkg.source == PackageSource.BuiltIn || pkg.source == PackageSource.Embedded)
                    continue;

                if (pkg.source == PackageSource.Git)
                {
                    // packageId = "name@url" 형식 — 동일 URL로 재추가하면 최신 커밋으로 갱신됨
                    packagesToAdd.Add(pkg.packageId);
                    _requestedNames.Add(pkg.name);
                    Debug.Log($"[PackageUpdater] Will refresh git package: {pkg.name}");
                }
                else
                {
                    // 현재 에디터 호환 최신 버전만 대상. versions.latest는 호환성을 무시해
                    // Unity 6 전용 버전(예: TMP 4.x, visualscripting 1.9.12)까지 끌어온다.
                    string target = pkg.versions.latestCompatible;
                    if (!string.IsNullOrEmpty(target) && target != pkg.version)
                    {
                        packagesToAdd.Add($"{pkg.name}@{target}");
                        _requestedNames.Add(pkg.name);
                        Debug.Log($"[PackageUpdater] Will update: {pkg.name}  {pkg.version} → {target}");
                    }
                }
            }

            if (packagesToAdd.Count == 0)
            {
                Debug.Log("[PackageUpdater] All target UPM stack packages are up to date.");
                UpdateNugetPackages();
                EditorUtility.ClearProgressBar();
                return;
            }

            // 패키지마다 Client.Add를 부르면 하나가 끝날 때마다 다시 해석·컴파일되어, 다음 요청이 "exclusive access" 오류로
            // 실패하거나 도메인 리로드로 남은 순서가 사라졌음. 한 번의 요청으로 모두 올려 해석·리로드를 한 번으로 줄임.
            Debug.Log($"[PackageUpdater] Updating {packagesToAdd.Count} UPM package(s)");
            EditorUtility.DisplayProgressBar("Package Updater", $"UPM 패키지 {packagesToAdd.Count}개 업데이트 중...", 0.3f);
            SessionState.SetBool(PendingNugetUpdateKey, true);
            _addRequest = Client.AddAndRemove(packagesToAdd.ToArray());
            EditorApplication.update += WaitForAdd;
        }

        static void WaitForAdd()
        {
            if (!_addRequest.IsCompleted) return;
            EditorApplication.update -= WaitForAdd;

            if (_addRequest.Status == StatusCode.Success)
            {
                foreach (UnityEditor.PackageManager.PackageInfo pkg in _addRequest.Result)
                {
                    if (_requestedNames.Contains(pkg.name))
                    {
                        Debug.Log($"[PackageUpdater] ✓ {pkg.name}@{pkg.version}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[PackageUpdater] UPM update failed: {_addRequest.Error.message}");
            }

            UpdateNugetPackages();
            EditorUtility.ClearProgressBar();
        }

#if HULIACDEV_NUGETFORUNITY
        /// <summary>
        /// NuGet 업데이트를 하는 최대 횟수. NuGetForUnity는 패키지를 다시 설치할 때 그 의존성을 nuspec의 최소 버전으로
        /// 다시 깔아, 같은 차례에 먼저 올린 의존성이 도로 내려갈 수 있음(예: ZLogger 2.6.0을 설치하며 System.IO.Hashing 10.x → 8.0.0).
        /// 업데이트가 없을 때까지 다시 확인해 최신으로 맞추되, 서로 내려 주며 끝나지 않는 경우를 막도록 업데이트 횟수를 둠.
        /// </summary>
        const int MaxNugetUpdatePasses = 3;
#endif

        /// <summary>
        /// NuGetForUnity 창의 Updates 탭과 같은 기준(정식 버전 중 설치된 것보다 새 버전)으로 NuGet 패키지를 모두 올림.
        /// NuGetForUnity가 없는 프로젝트에서는 건너뜀(asmdef versionDefines로 HULIACDEV_NUGETFORUNITY를 정의함).
        /// </summary>
        static void UpdateNugetPackages()
        {
            SessionState.SetBool(PendingNugetUpdateKey, false);

#if HULIACDEV_NUGETFORUNITY
            EditorUtility.DisplayProgressBar("Package Updater", "NuGet 패키지 업데이트 확인 중...", 0.8f);

            try
            {
                int updatedCount = 0;
                // 업데이트를 최대 MaxNugetUpdatePasses번 하고, 마지막 업데이트 뒤에도 한 번 더 확인함(확인은 최대 횟수 + 1번).
                for (int pass = 0; pass <= MaxNugetUpdatePasses; pass++)
                {
                    List<INugetPackage> installed = new List<INugetPackage>(InstalledPackagesManager.InstalledPackages);
                    List<INugetPackage> updates = FindNugetUpdates(installed);

                    if (updates.Count == 0)
                    {
                        Debug.Log(updatedCount == 0
                            ? "[PackageUpdater] All NuGet packages are up to date."
                            : $"[PackageUpdater] Updated NuGet packages ({updatedCount} in total)");
                        return;
                    }

                    if (pass == MaxNugetUpdatePasses)
                        break;

                    NugetPackageUpdater.UpdateAll(updates, installed);
                    updatedCount += updates.Count;
                }

                Debug.LogWarning($"[PackageUpdater] Updates still remain after {MaxNugetUpdatePasses} NuGet update passes. Check the Updates tab in NuGet > Manage NuGet Packages.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PackageUpdater] NuGet package update failed: {e}");
            }
#else
            Debug.Log("[PackageUpdater] NuGetForUnity is not installed; skipping NuGet package updates.");
#endif
        }

#if HULIACDEV_NUGETFORUNITY
        /// <summary>설치된 것보다 새 정식 버전이 있는 NuGet 패키지를 찾아 로그로 남기고 돌려줌.</summary>
        static List<INugetPackage> FindNugetUpdates(List<INugetPackage> installed)
        {
            List<INugetPackage> updates = new List<INugetPackage>();

            foreach (INugetPackage update in ConfigurationManager.GetUpdates(installed))
            {
                INugetPackage current = FindInstalled(installed, update.Id);
                if (current == null || current.PackageVersion >= update.PackageVersion)
                    continue;

                updates.Add(update);
                Debug.Log($"[PackageUpdater] NuGet update: {update.Id}  {current.Version} → {update.Version}");
            }

            return updates;
        }
#endif

#if HULIACDEV_NUGETFORUNITY
        /// <summary>설치된 NuGet 패키지 중 같은 ID(대소문자 무시)를 찾음. 없으면 null.</summary>
        static INugetPackage FindInstalled(List<INugetPackage> installed, string id)
        {
            foreach (INugetPackage package in installed)
            {
                if (string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase))
                    return package;
            }

            return null;
        }
#endif
    }
}
