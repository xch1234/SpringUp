using System.Collections;
using NUnit.Framework;
using SpringUp.Flow;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class RewardFlightTests
{
    [UnityTest] public IEnumerator PauseTransitionUnloadPoolLimitAndClear()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        var bootstrapScene = SceneManager.GetActiveScene();
        var canvas = new GameObject("Bootstrap test canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var settings = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<RewardFlightSettings>("Assets/Data/Rewards/RewardFlightSettings.asset"));
        settings.maxConcurrent = 2;
        using (var pause = new PauseService())
        {
            var presenter = RewardFlightPresenter.Create(canvas.transform, settings, pause); presenter.enabled = false;
            presenter.SpawnAtViewport(new Vector2(-1, 2), false);
            presenter.SpawnAtViewport(Vector2.one * .5f, true);
            presenter.SpawnAtViewport(Vector2.zero, false);
            Assert.AreEqual(2, presenter.ActiveCount, "Visual cap only");
            using (pause.Acquire()) { presenter.Advance(1); Assert.AreEqual(2, presenter.ActiveCount); }
            var battle = SceneManager.CreateScene("Reward flight unload test"); SceneManager.SetActiveScene(battle);
            var marker = new GameObject("Battle-only object");
            using (pause.Acquire(pauseRewardAnimations: false))
            {
                Assert.AreEqual(0, Time.timeScale);
                SceneManager.SetActiveScene(bootstrapScene);
                yield return SceneManager.UnloadSceneAsync(battle);
                Assert.IsTrue(marker == null); Assert.AreEqual(2, presenter.ActiveCount);
                presenter.Advance(.7f); Assert.AreEqual(0, presenter.ActiveCount);
            }
            presenter.SpawnAtViewport(Vector2.zero, true); Assert.AreEqual(1, presenter.ActiveCount);
            presenter.Clear(); Assert.AreEqual(0, presenter.ActiveCount);
            presenter.SpawnAtViewport(Vector2.one, false); presenter.Advance(.7f); Assert.AreEqual(0, presenter.ActiveCount);
        }
        Object.Destroy(canvas); Object.Destroy(settings); yield return null;
        yield return new ExitPlayMode();
    }
}
