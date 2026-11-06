using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace Killdg.MultipurposeDrone
{
    /// <summary>
    /// Short point-target engagements using each bot's existing firearm,
    /// ammunition, aiming and ballistics. No duplicate player, synthetic hit,
    /// health immunity or permanent faction/enemy-list change is introduced.
    /// </summary>
    internal sealed class DroneCombat
    {
        private sealed class Attention
        {
            internal float NextCheck;
            internal float Until;
            internal float FireAt;
            internal Vector3 Offset;
            internal bool OwnsTrigger;
            internal float NextSightCheck;
            internal bool HasSight;
            internal bool BurstLogged;
            internal bool RetreatAttempted;
            internal bool Retreating;
            internal float RetreatUntil;
        }

        internal static DroneCombat? Current;
        private readonly Dictionary<BotOwner, Attention> attention = new();
        private readonly Transform target;
        private readonly int mapMask;
        private int engaged;
        private readonly NavMeshPath retreatPath = new();

        internal DroneCombat(Transform target, int mapMask)
        {
            this.target = target;
            this.mapMask = mapMask;
        }

        internal void Tick(BotOwner owner)
        {
            if (target == null || owner.BotState != EBotState.Active
                || owner.GetPlayer?.HealthController?.IsAlive != true)
            {
                if (attention.TryGetValue(owner, out Attention inactive) && inactive.OwnsTrigger)
                {
                    inactive.OwnsTrigger = false;
                    engaged = Mathf.Max(0, engaged - 1);
                    attention.Remove(owner);
                }

                return;
            }

            if (!attention.TryGetValue(owner, out Attention state))
            {
                state = new Attention { NextCheck = Time.time + Random.Range(0.6f, 1.3f) };
                attention[owner] = state;
            }

            if (!state.OwnsTrigger && (Time.time < state.NextCheck || engaged >= 2))
            {
                return;
            }

            // A bot with a visible real opponent keeps fighting that opponent.
            // Finding the operator therefore remains dangerous for the player.
            Vector3 origin = owner.Fireport.position;
            Vector3 direction = target.position - origin;
            float distance = direction.magnitude;
            EnemyInfo? enemy = owner.Memory?.GoalEnemy;
            float priorityDistance = Mathf.Min(30, distance);
            bool realFight = enemy?.IsVisible == true
                && (enemy.EnemyLastPositionReal - origin).sqrMagnitude < priorityDistance * priorityDistance;
            if (Time.time >= state.NextSightCheck)
            {
                state.NextSightCheck = Time.time + 0.25f;
                state.HasSight = distance < 85 && distance > 0.5f
                    && !Physics.Raycast(origin, direction.normalized, Mathf.Max(0, distance - 0.5f),
                        mapMask, QueryTriggerInteraction.Ignore);
            }

            bool canSee = state.HasSight;
            if (realFight || !canSee || Time.time >= state.Until)
            {
                Release(owner, state);
                if (realFight || !canSee || Time.time < state.NextCheck || engaged >= 2)
                {
                    return;
                }

                state.NextCheck = Time.time + Random.Range(5f, 9f);
                if (owner.GetPlayer.HandsController is not Player.FirearmController
                    || distance > 15 && (Vector3.Dot(owner.LookDirection.normalized, direction.normalized) < -0.15f
                        || Random.value > Mathf.Lerp(0.75f, 0.3f, distance / 85)))
                {
                    return;
                }

                state.Until = Time.time + Random.Range(3.5f, 4.5f);
                state.FireAt = Time.time + Random.Range(1.0f, 1.6f);
                // This is extra aim error on top of native weapon/bot spread.
                // Nearby aircraft are easier to hit; distant ones are not lasers.
                state.Offset = Random.insideUnitSphere * Mathf.Lerp(0.12f, 2.4f, distance / 85);
                state.OwnsTrigger = true;
                state.BurstLogged = false;
                state.RetreatAttempted = false;
                engaged++;
                Debug.Log("[Recon Drone] AI acquired aircraft at " + distance.ToString("0.0") + " m.");
            }

            if (state.Retreating)
            {
                if (Time.time < state.RetreatUntil)
                {
                    return;
                }
                StopRetreat(owner, state);
            }
            if (state.BurstLogged && !state.RetreatAttempted && distance < 17)
            {
                state.RetreatAttempted = true;
                if (TryRetreat(owner, state))
                {
                    return;
                }
            }

            Vector3 point = target.position + state.Offset;
            owner.AimingManager.CurrentAiming.SetTarget(point);
            owner.AimingManager.NodeUpdate();
            // Idle native logic can overwrite steering before its own fixed
            // step. Apply this engagement's point turn afterward, without moving
            // the bot or changing its brain/enemy identity.
            owner.Steering.LookToPoint(point, 120f);
            owner.Steering.SetYByDir(direction);
            owner.Steering.ManualFixedUpdate();
            // Native idle logic can reset AimStatus every frame without an
            // IPlayer enemy. After a real reaction delay, use actual facing
            // alignment rather than waiting forever for that player-only state.
            bool aligned = Vector3.Dot(owner.LookDirection.normalized, direction.normalized) > 0.85f;
            if (Time.time >= state.FireAt && aligned)
            {
                bool previous = owner.ShootData.CanShootByState;
                try
                {
                    owner.ShootData.SetCanShootByState(true);
                    bool fired = owner.ShootData.Shoot();
                    if (fired && !state.BurstLogged)
                    {
                        state.BurstLogged = true;
                        Debug.Log("[Recon Drone] AI began native firing burst at aircraft.");
                    }
                }
                finally
                {
                    owner.ShootData.SetCanShootByState(previous);
                }
            }
        }

        private bool TryRetreat(BotOwner owner, Attention state)
        {
            if (owner.Mover.CurrentState != EBotMoverState.Default || owner.Medecine.Using
                || owner.BewareGrenade.ShallRunAway())
            {
                return false;
            }
            Vector3 away = owner.Position - target.position;
            away.y = 0;
            if (away.sqrMagnitude < 0.1f)
            {
                return false;
            }
            Vector3 destination = owner.Position + away.normalized * 9;
            if (!NavMesh.SamplePosition(destination, out NavMeshHit ground, 3, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(owner.Position, ground.position, NavMesh.AllAreas, retreatPath)
                || retreatPath.status != NavMeshPathStatus.PathComplete || retreatPath.corners.Length < 2)
            {
                return false;
            }
            // Alternate a native burst with a short retreat rather than forcing
            // sprint and fire animations simultaneously. Never teleport or block
            // a higher-priority native movement/combat action.
            owner.ShootData.EndShoot();
            owner.Mover.GoToByWay(retreatPath.corners, 0.8f);
            owner.MovementResume();
            owner.Sprint(true, false);
            state.Retreating = true;
            state.RetreatUntil = Time.time + 1.3f;
            return true;
        }

        private static void StopRetreat(BotOwner owner, Attention state)
        {
            if (state.Retreating)
            {
                state.Retreating = false;
                owner.Sprint(false, false);
            }
        }

        private void Release(BotOwner owner, Attention state)
        {
            if (!state.OwnsTrigger)
            {
                return;
            }

            StopRetreat(owner, state);
            state.OwnsTrigger = false;
            state.Until = 0;
            engaged = Mathf.Max(0, engaged - 1);
            owner.ShootData?.EndShoot();
            owner.AimingManager?.CurrentAiming?.LoseTarget();
            owner.Steering?.LookToMovingDirection();
        }

        internal void Dispose()
        {
            foreach (KeyValuePair<BotOwner, Attention> pair in attention)
            {
                if (pair.Key != null)
                {
                    Release(pair.Key, pair.Value);
                }
            }

            attention.Clear();
            if (Current == this)
            {
                Current = null;
            }
        }
    }

    [HarmonyPatch(typeof(BotOwner), nameof(BotOwner.UpdateManual))]
    internal static class DroneThreatUpdate
    {
        private static void Postfix(BotOwner __instance)
        {
            try
            {
                DroneCombat.Current?.Tick(__instance);
            }
            catch (System.Exception exception)
            {
                // Fail closed for this raid instead of repeatedly throwing from
                // a native bot update. The operator and normal AI stay active.
                DroneCombat.Current?.Dispose();
                Debug.LogError("[Recon Drone] Point targeting disabled: " + exception);
            }
        }
    }
}
