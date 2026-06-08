# Head-driven turning + smart mirror torso-follow

**Date:** 2026-06-08
**Project:** Titanic VR (see `titanic-vr-conversion`, `titanic-jack-rose-characters`)
**Status:** Design approved; ready for implementation plan.

## Problem

The player currently turns with the right thumbstick (`ContinuousTurnProvider` on the
XR Origin). We want to remove artificial stick-turning and let the player turn by
**physically pivoting** their body — the lab uses wireless Air Link with enough floor
space to rotate fully, so physical turning is viable and incurs **zero artificial
camera rotation** (no vection, no nausea — consistent with the earlier decision to drop
snap turn).

Removing the stick turn surfaces the only real difficulty: the mirror avatar. Today the
avatar's **head bone** tracks the HMD orientation, but the **torso never rotates**
(`AvatarRigDriver.followHmdHorizontal` repositions the root but does not yaw it). So when
the player physically turns 90–180°, the reflection would show an **owl neck** — head
cranked around while the shoulders stay frozen.

The two cases that must be distinguished, matching real mirror behavior:

1. **Glance** — head turns left/right briefly/modestly; shoulders should stay put
   (you check yourself in the mirror without your body following).
2. **Commit** — head turns far / the player physically rotates to face/walk a new way;
   the torso should swing around to follow, even up to 180°.

Because the HMD only reports head orientation (never the real torso), the torso facing
must be **inferred** from head facing. The glance-vs-commit distinction is that inference.

## Goals

- Delete artificial stick-turn entirely. No artificial-turn fallback (no snap, no
  comfort panel — already dropped previously).
- Keep locomotion head-relative (unchanged `DynamicMoveProvider`): "forward" is wherever
  the player is physically facing.
- Make the mirror avatar's torso behave like a real mirror: glance = shoulders hold,
  commit = torso follows smoothly. No owl neck. Applies uniformly to Default/Jack/Rose.

## Non-goals

- No artificial view rotation of any kind (head-steer / lean-to-turn explicitly rejected).
- No changes to the `IPoseSource` seam, the locomotion/move provider, collision, or the
  mirror reflection shader.
- No leg/foot stepping to reorient (the torso yaw is cosmetic to the reflection; legs stay
  in the existing locomotion/idle animation).

## Design

Two independent changes.

### 1. Remove artificial turn

Disable/remove the `ContinuousTurnProvider` component on the `XR Origin (XR Rig)`.
Locomotion stays head-relative via the existing `DynamicMoveProvider` — no change there.
The right thumbstick becomes unused (left unbound; no repurposing — YAGNI).

This is the same kind of edit already done when `SnapTurnProvider` was removed.

### 2. Smart torso-follow on the mirror avatar (the only real work)

In `AvatarRigDriver`, replace the position-only `followHmdHorizontal` behavior with
**position + hysteresis-gated yaw** on the avatar root:

- Compute `desiredYaw` = the HMD's yaw flattened to the floor plane (ignore pitch/roll).
- Compute `offset` = signed angle between `desiredYaw` and the current root yaw.
- State machine with hysteresis (two thresholds):
  - **Frozen** (default): if `|offset| < startAngle` → root yaw held (glance: shoulders
    stay). When `|offset| ≥ startAngle` → transition to Following.
  - **Following**: smoothly rotate root yaw toward `desiredYaw`, rate-limited
    (`maxYawSpeed`, deg/s). When `|offset| < stopAngle` → transition back to Frozen.
- The large-start / small-stop gap (e.g. `startAngle ≈ 40°`, `stopAngle ≈ 5°`) is what
  separates a glance (never crosses start) from a commit (crosses start, then runs all
  the way down to stop). No timers needed.

Head and hand bones are untouched (still written absolutely to HMD/controller orientation
in `LateUpdate`). So during a glance the reflection shows a natural neck twist; during a
commit the shoulders swing around to match the head.

Root **position** still follows the HMD horizontally exactly as today.

All tuning values are serialized fields on `AvatarRigDriver` so they can be adjusted live
in-headset:

- `followYaw` (bool, default true) — enable the new yaw behavior.
- `startAngle` (deg, ≈40) — head-vs-torso offset at which the torso starts following.
- `stopAngle` (deg, ≈5) — offset at which it stops following.
- `maxYawSpeed` (deg/s, ≈180) — how fast the torso swings while following.

Optional enhancement (only if needed after testing, not in the core): tighten `startAngle`
or raise `maxYawSpeed` while the player is moving, so the torso aligns faster to travel
direction when walking. Deferred unless in-headset testing shows the standing-only tuning
feels wrong while walking.

### Why this is well-bounded

The change is confined to `AvatarRigDriver` plus one scene edit (drop the turn provider).
It affects only the mirror-only (layer 8) avatar, so it cannot affect comfort, collision,
or the locomotion system. The `IPoseSource` seam is untouched. Because all three avatars
share the driver, they all get the behavior with no per-character work (beyond the existing
calibrated offsets, which are unaffected — this is root yaw, not bone offsets).

## Verification (in-headset)

1. **Glance:** look left/right at the mirror within ~40° → reflected shoulders hold; natural
   neck twist; torso does not drift.
2. **Commit:** physically turn ~180° → reflected torso swings smoothly to face the new
   direction, ends aligned with the head (no residual owl neck), settles cleanly.
3. **Locomotion:** push the stick while facing any physical direction → walk that way, as
   before. No artificial turn occurs at any point.
4. **All avatars:** Default, Jack, and Rose all behave identically.
5. **Console clean**; framerate unaffected.

## Risks / open points

- **Neck-twist plausibility during the lag window.** While Following, the torso trails the
  head briefly; with `maxYawSpeed ≈ 180°/s` this should read as a natural shoulder-trails-head
  motion. If it looks robotic or too laggy, tune `maxYawSpeed` / smoothing in-headset.
- **`startAngle` choice.** Too small → shoulders twitch on ordinary glances; too large →
  visible owl neck before the torso catches up. ~40° is the starting estimate; finalize by
  testing.
- **`DynamicMoveProvider` forward source.** Confirm during implementation that move-forward is
  head-relative (expected). If it references the rig/origin yaw, removing the turn provider
  should not matter, but verify movement direction tracks head facing.
