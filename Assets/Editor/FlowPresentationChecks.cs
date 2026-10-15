using System;
using System.IO;
using System.Linq;
using SpringUp.Flow;
using SpringUp.Organs;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace SpringUp.EditorChecks
{
    public static class FlowPresentationChecks
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            Directory.CreateDirectory("Logs/Presentation");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var map = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FlowPresentationRepair.MapPath));
            var tiles = map.GetComponentsInChildren<SpriteRenderer>();
            Require(tiles.Length == 400, "20 x 20 cells");
            Require(tiles.Count(t => t.sprite.name == "5") == 324, "18 x 18 repeatable center");
            foreach (int id in new[] {1,3,7,9}) Require(tiles.Count(t => t.sprite.name == id.ToString()) == 1, "One tile per corner");
            foreach (int id in new[] {2,4,6,8}) Require(tiles.Count(t => t.sprite.name == id.ToString()) == 18, "Repeated edges");
            foreach (var tile in tiles) Require(Vector2.Distance(tile.bounds.size, new Vector2(3.6f,3.6f)) < .001f, "Equal native-aspect tiles");
            var camera = new GameObject("Capture camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10); camera.orthographic = true; camera.orthographicSize = 37;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.03f,.04f,.05f);
            Capture(camera, 1000,1000,"arena.png"); Object.DestroyImmediate(map);
            var canvas = new GameObject("Verification canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            var lab = Object.Instantiate(AssetDatabase.LoadAssetAtPath<FlowLaboratoryWindow>(FlowPresentationRepair.LabPath), canvas.transform);
            var config = new RunConfiguration(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Organs/organs.json").text,
                AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Waves/waves.json").text, OrganCatalog.LoadDefault());
            var run = config.CreateRun(); run.StartRun(); run.CombatTimerFinished();
            for (int i = 0; i < 30; i++) run.Inventory.Spawn("rust_knife");
            lab.presenter.Bind(new RunLaboratoryEquipment(run, config.Definitions));
            lab.status.text = "背包超限测试 · 请选择物品和对应槽位安装。\n\n关闭背包后在休整地图攻击木桩。\n打开背包时世界暂停。";
            lab.gameObject.SetActive(true);
            foreach (var size in new[] {new Vector2Int(1440,900),new Vector2Int(1280,720),new Vector2Int(1024,768)})
            {
                canvas.scaleFactor = Mathf.Min(size.x / 1440f, size.y / 900f);
                Capture(camera,size.x,size.y,$"laboratory-{size.x}.png", () => {
                    var scroll = lab.presenter.inventoryRoot.GetComponentInParent<ScrollRect>();
                    Require(lab.presenter.inventoryRoot.childCount == run.Inventory.UnequippedIds().Count,"Every inventory item has a card");
                    var corners = new Vector3[4];
                    foreach (var rect in new[] {scroll.viewport, (RectTransform)lab.closeButton.transform, (RectTransform)lab.presenter.installButton.transform})
                    {
                        rect.GetWorldCorners(corners);
                        foreach (var corner in corners) { var p = camera.WorldToViewportPoint(corner); Require(p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1,"UI stays on screen: " + rect.name + " at " + p + " resolution " + size); }
                    }
                    scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
                    var last = (RectTransform)lab.presenter.inventoryRoot.GetChild(lab.presenter.inventoryRoot.childCount-1);
                    last.GetWorldCorners(corners);
                    foreach (var corner in corners) { var p=scroll.viewport.InverseTransformPoint(corner); Require(p.y >= scroll.viewport.rect.yMin-1 && p.y <= scroll.viewport.rect.yMax+1,"Overflow last card reachable"); }
                    scroll.verticalNormalizedPosition = 1;
                });
            }
            Debug.Log("[Presentation Checks] PASS: tile counts, sizes, UI bounds and overflow scrolling at three resolutions; captures saved.");
        }
        static void Capture(Camera camera, int width, int height, string name, Action validate = null)
        {
            var texture = new RenderTexture(width,height,24); var pixels = new Texture2D(width,height,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = texture; Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = texture; pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
                File.WriteAllBytes("Logs/Presentation/"+name,pixels.EncodeToPNG()); validate?.Invoke();
            }
            finally { camera.targetTexture=null; RenderTexture.active=previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels); }
        }
    }
}
