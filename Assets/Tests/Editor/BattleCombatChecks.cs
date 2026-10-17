using System;
using System.Collections.Generic;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = SpringUp.Organs.BodyPart;
using OrganDefinition = SpringUp.Organs.OrganDefinition;
using OrganInstance = SpringUp.Organs.OrganInstance;

namespace SpringUp.EditorChecks
{
    public static class BattleCombatChecks
    {
        public static void RunPlay()
        {
            var scene = SceneManager.CreateScene("Battle combat checks");
            var objects = new List<GameObject>();
            GameObject Go(string name)
            {
                var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); objects.Add(go); return go;
            }
            EnemyRuntime Enemy(string name, Vector2 position, float hp = 20)
            {
                var go = Go(name); go.SetActive(false); go.transform.position = position;
                var data = go.AddComponent<EnemyData>();
                var edit = new SerializedObject(data); edit.FindProperty("maxHealth").floatValue = hp; edit.ApplyModifiedPropertiesWithoutUndo();
                var enemy = go.AddComponent<EnemyRuntime>(); go.SetActive(true); return enemy;
            }
            try
            {
                var root = Go("Combat");
                var combat = root.AddComponent<BattleOrganCombat>();
                root.GetComponent<BattleOrganTargeting>().AttackOrigin = root.transform;
                var pipeline = root.GetComponent<OrganPipelineController>(); pipeline.enabled = false;
                var persistent = Enemy("single cast bleed", new Vector2(20, 0), 100);
                combat.Resolve(new CastEvent(persistent.TargetId, 1, "knife", "once",
                    status: new StatusEffectData(StatusEffectKind.Bleed, 1, 2, tickDamage: 2)));
                combat.Advance(.5f); Require(persistent.CurrentHealth == 99, "单次施加状态不会提前跳伤害。");
                combat.Advance(.5f); Require(persistent.CurrentHealth == 97, "管道停用且不再攻击，流血仍持续。");
                combat.Advance(1); Require(persistent.CurrentHealth == 95, "状态按自身时长结算末次伤害。");
                combat.Advance(2); Require(persistent.CurrentHealth == 95, "状态到期停止，无需再次触发链。");
                persistent.gameObject.SetActive(false); combat.Clear();
                var a = Enemy("a", new Vector2(2, 0));
                var b = Enemy("b", new Vector2(2.5f, 0));
                var c = Enemy("c", new Vector2(-.5f, 0));
                var explosion = new CastEvent(a.TargetId, 5, "tnt", "tnt", shape: AttackShape.Explosion,
                    radius: 1, effectCenter: a.Position);
                combat.Resolve(explosion);
                Require(a.CurrentHealth == 15 && b.CurrentHealth == 15 && c.CurrentHealth == 20, "真实范围扣血且主目标只扣一次。");
                var slow = new CastEvent(a.TargetId, 1, "slime", "slime", status: new StatusEffectData(StatusEffectKind.Slow, 1, 1,
                    slowPerStack: .15f, maxStacks: 3));
                combat.Resolve(slow); combat.Resolve(slow);
                Require(Mathf.Approximately(a.OrganSpeedMultiplier, .7f), "减速影响真实移动倍率。");
                combat.Advance(1); Require(Mathf.Approximately(a.OrganSpeedMultiplier, .85f), "减速逐层衰减。");
                var stun = new CastEvent(a.TargetId, 1, "steel", "steel", status: new StatusEffectData(StatusEffectKind.Stun, 1, 1));
                combat.Resolve(stun); Require(a.OrganSpeedMultiplier == 0, "眩晕阻止移动。");
                combat.Resolve(new CastEvent(a.TargetId, 0, "collapse", "hole", shape: AttackShape.BlackHole, radius: 3,
                    blackHole: new BlackHoleData(1, 2), effectCenter: new Vector2(1, 0)));
                combat.Advance(.25f); Require(a.Position.x < 2 && a.CurrentHealth == 12, "黑洞外力可移动眩晕目标且不扣血。");
                string oldId = a.TargetId;
                a.gameObject.SetActive(false); a.gameObject.SetActive(true);
                combat.Resolve(slow); combat.Advance(2);
                Require(a.TargetId != oldId && a.CurrentHealth == 20 && a.OrganSpeedMultiplier == 1, "复用拒绝旧身份并清除状态。");
                a.gameObject.SetActive(false); b.gameObject.SetActive(false); c.gameObject.SetActive(false);
                combat.Clear();
                var dying = Enemy("bleeding", Vector2.one, 2);
                var hand = pipeline.GetBody(BodyPart.Hand);
                hand.SetSlot(0, new OrganInstance(new OrganDefinition("tnt", "TNT", BodyPart.Hand, OrganType.Actuator, 5,
                    shape: AttackShape.Explosion, radius: 1)));
                hand.SetSlot(1, new OrganInstance(new OrganDefinition("tentacle", "触手", BodyPart.Hand, OrganType.Trigger)));
                hand.SetSlot(2, new OrganInstance(hand.GetSlot(0).Definition));
                pipeline.enabled = true;
                var bleed = new CastEvent(dying.TargetId, 1, "knife", hand.GetSlot(0).InstanceId, status: new StatusEffectData(StatusEffectKind.Bleed, 1, 3, tickDamage: 1));
                CastEvent extra = default; int triggers = 0;
                pipeline.AttackProduced += attack => extra = attack;
                pipeline.TriggerActivated += (_, __, ___, ____) => triggers++;
                combat.Resolve(bleed); dying.transform.position = new Vector3(8, 4, 0);
                combat.Advance(1);
                Require(extra.EffectCenter == new Vector2(8, 4) && extra.ChainId == bleed.ChainId && triggers == 1,
                    "真实流血击杀保留原链与死亡位置。");
                var cleared = Enemy("clear", Vector2.zero);
                cleared.RemoveFromBattlefield(); Require(triggers == 1, "清场不能触发击杀链。");
                combat.enabled = false;
                Require(combat.ActiveBlackHoles == 0, "停用清理持续效果。");
                Debug.Log("[Battle Combat Checks] PLAY PASS: damage, area, slow, stun, black hole, pooling, bleed chain, cleanup.");
            }
            finally
            {
                foreach (var go in objects) if (go != null) { go.SetActive(false); UnityEngine.Object.Destroy(go); }
                SceneManager.UnloadSceneAsync(scene);
            }
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("[Battle Combat Checks] " + message); }
    }
}
