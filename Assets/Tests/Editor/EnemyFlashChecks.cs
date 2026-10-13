using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpringUp.EditorChecks
{
    public sealed class EnemyFlashChecks
    {
        [UnityTest]
        public IEnumerator DamageFlashRestoresColorAndSurvivesReuse()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Time.timeScale = 1;
            var enemies = new List<EnemyRuntime>();
            var colors = new List<Color>();
            try
            {
                foreach (var prefab in Resources.LoadAll<GameObject>("Enemies"))
                {
                    var go = Object.Instantiate(prefab, Vector3.right * enemies.Count * 10, Quaternion.identity);
                    enemies.Add(go.GetComponent<EnemyRuntime>());
                    colors.Add(go.GetComponent<SpriteRenderer>().color);
                    if (go.TryGetComponent<EnemyProducer>(out var producer)) producer.enabled = false;
                    if (go.TryGetComponent<EnemyHealer>(out var healer)) healer.enabled = false;
                }
                Assert.AreEqual(6, enemies.Count);
                var first = enemies[0];
                first.TakeDamage(0, "fist", first.Position);
                Assert.AreEqual(colors[0], first.GetComponent<SpriteRenderer>().color, "零伤害不闪色。");
                first.TakeAttack(new CastEvent(first.TargetId, 1, "fist", "flash-check"));
                Assert.AreEqual(Color.white, first.GetComponent<SpriteRenderer>().color, "真实器官命中当帧闪白。");
                for (int i = 1; i < enemies.Count; i++)
                    Assert.AreEqual(colors[i], enemies[i].GetComponent<SpriteRenderer>().color, "其他敌人不会跟着闪。");
                foreach (var enemy in enemies)
                    enemy.TakeDamage(1, "fist", enemy.Position);
                foreach (var enemy in enemies)
                    Assert.AreEqual(Color.white, enemy.GetComponent<SpriteRenderer>().color, "六种敌人均可闪白。");

                float resumeAt = Time.time + .7f;
                while (Time.time < resumeAt) yield return null;
                for (int i = 0; i < enemies.Count; i++)
                    Assert.AreEqual(colors[i], enemies[i].GetComponent<SpriteRenderer>().color, "闪色结束恢复各自原色。");

                int priorityChecks = 0;
                foreach (var enemy in enemies)
                {
                    var attack = enemy.GetComponent<EnemyAttack>();
                    if (attack == null || !attack.IsReady) continue;
                    enemy.TakeDamage(1, "fist", enemy.Position);
                    Assert.IsTrue(attack.TryAttack());
                    Assert.AreEqual(Color.white, enemy.GetComponent<SpriteRenderer>().color, "受伤闪色不被同帧开火覆盖。");
                    priorityChecks++;
                }
                Assert.Greater(priorityChecks, 0, "至少验证一种远程敌人的开火与受伤优先级。");

                first.TakeDamage(1, "fist", first.Position);
                resumeAt = Time.time + .08f;
                while (Time.time < resumeAt) yield return null;
                first.TakeDamage(1, "fist", first.Position);
                Assert.AreEqual(Color.white, first.GetComponent<SpriteRenderer>().color, "连续命中重新闪白。");
                first.gameObject.SetActive(false);
                Assert.AreEqual(colors[0], first.GetComponent<SpriteRenderer>().color, "回池立即恢复原色。");
                first.gameObject.SetActive(true);
                first.TakeDamage(1, "fist", first.Position);
                Assert.AreEqual(Color.white, first.GetComponent<SpriteRenderer>().color, "复用后仍响应命中。");
                first.GetComponent<EnemyAttackFlash>().enabled = false;
                Assert.AreEqual(colors[0], first.GetComponent<SpriteRenderer>().color, "停用闪色组件恢复原色。");
                first.GetComponent<EnemyAttackFlash>().enabled = true;
                resumeAt = Time.time + .25f;
                while (Time.time < resumeAt) yield return null;
                Assert.AreEqual(colors[0], first.GetComponent<SpriteRenderer>().color, "旧协程不会在重新启用后残留。");
            }
            finally
            {
                foreach (var enemy in enemies)
                    if (enemy != null) Object.Destroy(enemy.gameObject);
                Time.timeScale = 1;
            }
            yield return new ExitPlayMode();
        }
    }
}
