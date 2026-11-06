using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneTracking
    {
        private readonly List<Player> visibleActors = new();
        private readonly RaycastHit[] sightHits = new RaycastHit[32];
        private float nextSample;
        private Player? markedActor;
        private Vector2 viewSlope;
        private bool samplingDroneView;

        internal void Sample(GameWorld world, Camera camera, Transform drone, int mask, bool refresh = false)
        {
            float verticalSlope = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            viewSlope = new Vector2(verticalSlope * camera.aspect, verticalSlope);
            Sample(world, camera.transform, drone, mask, camera, true, refresh);
        }

        internal void SampleRelay(GameWorld world, Transform view, Transform drone, int mask, bool autoTrack)
        {
            Sample(world, view, drone, mask, null, autoTrack, false);
        }

        private void Sample(GameWorld world, Transform view, Transform drone, int mask,
            Camera? camera, bool allActors, bool refresh)
        {
            bool droneView = camera != null;
            refresh |= droneView != samplingDroneView;
            samplingDroneView = droneView;
            if (!refresh && Time.time < nextSample)
            {
                return;
            }

            nextSample = Time.time + 0.2f;
            visibleActors.Clear();
            if (!allActors)
            {
                if (markedActor != null)
                {
                    AddVisibleActor(markedActor, view, drone, mask, camera);
                }
                return;
            }

            foreach (var candidate in world.RegisteredPlayers)
            {
                if (candidate is Player actor && actor.IsAI)
                {
                    AddVisibleActor(actor, view, drone, mask, camera);
                }
            }
        }

        private void AddVisibleActor(Player actor, Transform view, Transform drone, int mask, Camera? camera)
        {
            if (actor.HealthController?.IsAlive != true)
            {
                return;
            }

            Vector3 origin = view.position;
            Vector3 chest = actor.Position + Vector3.up;
            Vector3 head = actor.Position + Vector3.up * 1.65f;
            if ((chest - origin).sqrMagnitude >= 40000)
            {
                return;
            }

            // When the PMC has the camera back, project against the drone's last
            // lens settings and its current hover pose without moving the camera.
            bool visible = camera != null
                ? TryGetBox(camera, actor, out _)
                    && (HasClearSight(origin, chest, actor, drone, mask)
                        || HasClearSight(origin, head, actor, drone, mask))
                : (InsideDroneView(view, chest) && HasClearSight(origin, chest, actor, drone, mask))
                    || (InsideDroneView(view, head) && HasClearSight(origin, head, actor, drone, mask));
            if (visible)
            {
                visibleActors.Add(actor);
            }
        }

        private bool InsideDroneView(Transform view, Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(view.rotation) * (point - view.position);
            return local.z > 0f
                && Mathf.Abs(local.x) <= local.z * viewSlope.x
                && Mathf.Abs(local.y) <= local.z * viewSlope.y;
        }

        internal bool MarkTarget(Camera camera)
        {
            Player? target = SelectTarget(camera, requireReticle: true);
            if (target == null)
            {
                return false;
            }

            markedActor = target;
            return true;
        }

        internal void Clear()
        {
            markedActor = null;
            visibleActors.Clear();
            nextSample = 0f;
            viewSlope = Vector2.zero;
        }

        internal bool HasClearSight(Vector3 origin, Vector3 point, Player actor, Transform drone, int mask)
        {
            Vector3 ray = point - origin;
            int count = Physics.RaycastNonAlloc(origin, ray.normalized, sightHits,
                ray.magnitude, mask, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length)
            {
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                Collider collider = sightHits[index].collider;
                if (collider != null && !collider.transform.IsChildOf(drone)
                    && collider.GetComponentInParent<Player>() != actor)
                {
                    return false;
                }
            }

            return true;
        }

        internal bool HasVisibleTarget(Camera camera)
        {
            foreach (Player actor in visibleActors)
            {
                if (TryGetBox(camera, actor, out _))
                {
                    return true;
                }
            }

            return false;
        }

        internal Player? SelectTarget(Camera camera, bool requireReticle = false)
        {
            var reticle = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Player? target = null;
            float bestScore = float.PositiveInfinity;
            foreach (Player actor in visibleActors)
            {
                if (!TryGetBox(camera, actor, out Rect box))
                {
                    continue;
                }

                // Prefer a box under the reticle, then the nearest box to it.
                float score = (box.center - reticle).sqrMagnitude;
                if (!box.Contains(reticle))
                {
                    if (requireReticle)
                    {
                        continue;
                    }

                    score += (float)Screen.width * Screen.width + (float)Screen.height * Screen.height;
                }

                if (score < bestScore)
                {
                    target = actor;
                    bestScore = score;
                }
            }
            return target;
        }

        private static bool TryGetBox(Camera camera, Player actor, out Rect box)
        {
            box = default;
            if (actor == null || actor.HealthController?.IsAlive != true)
            {
                return false;
            }

            Vector3 feet = camera.WorldToScreenPoint(actor.Position);
            Vector3 head = camera.WorldToScreenPoint(actor.Position + Vector3.up * 1.85f);
            if (feet.z <= 0 || head.z <= 0)
            {
                return false;
            }

            float height = Mathf.Max(14f, Mathf.Abs(head.y - feet.y));
            float width = Mathf.Max(10f, height * 0.45f);
            box = new Rect(feet.x - width / 2, Screen.height - (head.y + feet.y) * 0.5f - height * 0.5f, width, height);
            return box.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
        }

        internal void Draw(Camera camera, bool relay = false, bool autoTrack = false)
        {
            if (Event.current.type != EventType.Repaint || (relay && samplingDroneView))
            {
                return;
            }
            Color previous = GUI.color;
            foreach (Player actor in visibleActors)
            {
                if (relay && !autoTrack && actor != markedActor)
                {
                    continue;
                }

                if (!TryGetBox(camera, actor, out Rect box))
                {
                    continue;
                }

                float thickness = Mathf.Max(2f, Screen.height / 540f);
                GUI.color = new Color(0.02f, 0.07f, 0.03f, 0.22f);
                DrawBorder(box, thickness + 2f);
                GUI.color = actor == markedActor
                    ? new Color(0.35f, 1f, 0.75f, 0.65f)
                    : new Color(0.32f, 1f, 0.28f, 0.45f);
                DrawBorder(box, thickness);
            }
            GUI.color = previous;
        }

        private static void DrawBorder(Rect box, float thickness)
        {
            var texture = Texture2D.whiteTexture;
            float edge = thickness * 0.5f;
            GUI.DrawTexture(new Rect(box.x - edge, box.y - edge, box.width + thickness, thickness), texture);
            GUI.DrawTexture(new Rect(box.x - edge, box.yMax - edge, box.width + thickness, thickness), texture);
            GUI.DrawTexture(new Rect(box.x - edge, box.y, thickness, box.height), texture);
            GUI.DrawTexture(new Rect(box.xMax - edge, box.y, thickness, box.height), texture);
        }
    }
}
