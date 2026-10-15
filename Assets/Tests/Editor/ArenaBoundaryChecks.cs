using System;
using System.Linq;
using SpringUp.Flow;
using TideBorne.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpringUp.EditorChecks
{
    public static class ArenaBoundaryChecks
    {
        // Exercise the real motor sweep and physics integration in an isolated physics scene.
        public static void Run(FlowContent source)
        {
            var scene = SceneManager.CreateScene("Boundary verification", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            try
            {
                var walls = source.world.GetComponentsInChildren<BoxCollider2D>()
                    .Where(c => c.name.StartsWith("Wall_")).ToArray();
                if (walls.Length != 4) throw new Exception("Expected four arena wall colliders");
                foreach (var original in walls)
                {
                    var wall = new GameObject(original.name); SceneManager.MoveGameObjectToScene(wall, scene);
                    wall.layer = original.gameObject.layer;
                    wall.transform.position = original.transform.position; wall.transform.localScale = original.transform.lossyScale;
                    wall.AddComponent<BoxCollider2D>().size = original.size;
                }
                var go = new GameObject("Sweep test player"); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<PlayerStatsBase>();
                var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = source.player.GetComponent<BoxCollider2D>().size;
                go.transform.localScale = source.player.transform.lossyScale;
                var motor = go.AddComponent<PlayerMotor>(); motor.enabled = false;
                go.SetActive(true);
                var physics = scene.GetPhysicsScene2D();
                foreach (float speed in new[] { 15f, 2000f })
                foreach (var direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                    new Vector2(1,1), new Vector2(-1,1), new Vector2(1,-1), new Vector2(-1,-1) })
                {
                    body.position = Vector2.zero; body.rotation = direction.x * direction.y != 0 ? 45 : 0;
                    for (int i = 0; i < 240; i++)
                    {
                        body.linearVelocity = motor.ConstrainToWalls(direction.normalized * speed, .02f);
                        physics.Simulate(.02f);
                        if (Mathf.Abs(body.position.x) > 35.5f || Mathf.Abs(body.position.y) > 35.5f)
                            throw new Exception($"Escaped {source.kind} boundary: {body.position}, speed {speed}");
                    }
                    if (body.position.magnitude < 30) throw new Exception("Sweep should allow reaching the wall");
                }
                body.position = new Vector2(35,0); body.rotation = 0;
                for (int i = 0; i < 50; i++)
                {
                    body.linearVelocity = motor.ConstrainToWalls(new Vector2(15,15), .02f);
                    physics.Simulate(.02f);
                }
                if (body.position.y < 10 || body.position.x > 35.5f) throw new Exception("Expected movement along the wall");
                Debug.Log($"[Arena Boundary Checks] PASS {source.kind}: four walls, corners, sprint/high speed and sliding.");
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
