using MelonLoader;
using UnityEngine;
using UnityEngine.VFX;
using RumbleModdingAPI;

namespace RumbleAnimator.Recording;

public class Components
{
    [RegisterTypeInIl2Cpp]
    public class DisableOnHit : MonoBehaviour
    {
        public GameObject disableObject;
        public float punchSpeedThreshold = 1.5f;
        public VisualEffect destroyVFX;

        private void OnTriggerEnter(Collider other)
        {
            Transform lPhysicsHand = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(4).GetChild(2);
            Transform rPhysicsHand = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(4).GetChild(3);
            
            MelonLogger.Msg($"Collider name: {other.name} | IsHit: {IsHit(lPhysicsHand, other, true) || IsHit(rPhysicsHand, other, false)} | lPhysicsHand magnitude: {lPhysicsHand.GetComponent<Rigidbody>().velocity.magnitude} | rPhysicsHand magnitude: {rPhysicsHand.GetComponent<Rigidbody>().velocity.magnitude}");
            
            if (IsHit(lPhysicsHand, other, true) || IsHit(rPhysicsHand, other, false))
            {
                Disable();
            }
        }

        private bool ContainsLeftOrRight(GameObject other, bool left)
        {
            return other.name.Contains(left ? "L" : "R") || other.name.Contains(left ? "Left" : "Right") || other.transform.parent.name.Contains(left ? "L" : "R") || other.transform.parent.name.Contains(left ? "Left" : "Right");
        }

        private bool IsHit(Transform PhysicsHand, Collider other, bool left)
        {
            return PhysicsHand.GetComponent<Rigidbody>().velocity.magnitude > punchSpeedThreshold && ContainsLeftOrRight(other.gameObject, left);
        }

        public void Disable()
        {
            destroyVFX?.Play();
            disableObject.gameObject.SetActive(false);
        }
    }

//     [RegisterTypeInIl2Cpp]
//     public class BraceletVisuals : MonoBehaviour
//     {
//         private Renderer renderer;
//         public float dissolveDuration = 1f;
//         
//         private MaterialPropertyBlock block;
//         private static readonly int DissolveID = Shader.PropertyToID("_DissolveAmount");
//         private static readonly int PulseID = Shader.PropertyToID("_ShouldPulse");
//
//         private object dissolveRoutine;
//
//         public void Initialize()
//         {
//             block = new MaterialPropertyBlock();
//             renderer = GetComponent<Renderer>();
//             block.SetFloat(DissolveID, 1f);
//             block.SetFloat(PulseID, 0f);
//             renderer.SetPropertyBlock(block);
//         }
//
//         public void StartPulse()
//         {
//             if (dissolveRoutine != null)
//                 MelonCoroutines.Stop(dissolveRoutine);
//
//             dissolveRoutine = MelonCoroutines.Start(FadeDissolve(1f, 0f, () => SetPulse(true)));
//         }
//         
//         public void StopPulse()
//         {
//             if (dissolveRoutine != null)
//                 MelonCoroutines.Stop(dissolveRoutine);
//
//             dissolveRoutine = MelonCoroutines.Start(FadeDissolve(0f, 1f, () => SetPulse(false)));
//         }
//
//         private IEnumerator FadeDissolve(float from, float to, Action onComplete)
//         {
//             float t = 0f;
//
//             while (t < 1f)
//             {
//                 t += Time.deltaTime / dissolveDuration;
//                 float eased = SmoothStep(from, to, t);
//
//                 block.SetFloat(DissolveID, eased);
//                 renderer.SetPropertyBlock(block);
//
//                 yield return null;
//             }
//
//             block.SetFloat(DissolveID, to);
//             renderer.SetPropertyBlock(block);
//             
//             onComplete?.Invoke();
//         }
//
//         private void SetPulse(bool active)
//         {
//             block.SetFloat(PulseID, active ? 1f : 0f);
//             renderer.SetPropertyBlock(block);
//         }
//     }
} 