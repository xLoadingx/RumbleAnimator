using System.Collections;
using Il2CppRUMBLE.Utilities;
using Il2CppSystem.Buffers;
using MelonLoader;
using UnityEngine;
using UnityEngine.VFX;
using RumbleModdingAPI;
using static UnityEngine.Mathf;
using Utilities = RumbleAnimator.Utils.Utilities;

namespace RumbleAnimator.Recording;

public class Components
{
    [RegisterTypeInIl2Cpp]
    public class TagHolder : MonoBehaviour
    {
        public string holder;
    }
    
    [RegisterTypeInIl2Cpp]
    public class DisableOnHit : MonoBehaviour
    {
        public GameObject disableObject;
        public float punchSpeedThreshold = 1.5f;
        public VisualEffect destroyVFX;

        private Transform lPhysicsHand;
        private Transform rPhysicsHand;

        public void Initialize()
        {
            lPhysicsHand = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(4).GetChild(2);
            rPhysicsHand = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(4).GetChild(3);
        }

        private void OnTriggerEnter(Collider other)
        {
            MelonLogger.Msg($"Collider name: {other.name} | IsHit: {IsHit(lPhysicsHand, other, true) || IsHit(rPhysicsHand, other, false)} | lPhysicsHand magnitude: {lPhysicsHand.GetComponent<Rigidbody>().velocity.magnitude} | rPhysicsHand magnitude: {rPhysicsHand.GetComponent<Rigidbody>().velocity.magnitude}");
            
            if (IsHit(lPhysicsHand, other, true) || IsHit(rPhysicsHand, other, false) && disableObject.activeSelf)
            {
                Disable();
            }
        }

        private bool ContainsLeftOrRight(GameObject obj, bool left)
        {
            string leftStr = left ? "L" : "R";
            string leftFullStr = left ? "Left" : "Right";

            bool nameMatch = obj.name.Contains(leftStr) || obj.name.Contains(leftFullStr);
            bool parentMatch = obj.transform.parent != null && 
                               (obj.transform.parent.name.Contains(leftStr) || obj.transform.parent.name.Contains(leftFullStr));

            return nameMatch || parentMatch;
        }

        private bool IsHit(Transform PhysicsHand, Collider other, bool left)
        {
            return PhysicsHand.GetComponent<Rigidbody>().velocity.magnitude > punchSpeedThreshold && ContainsLeftOrRight(other.gameObject, left);
        }

        public void Disable()
        {
            destroyVFX?.Play();
            MelonCoroutines.Start(Utilities.PlaySound("Slab_Dismiss.wav"));
            disableObject.gameObject.SetActive(false);
        }
    }

     [RegisterTypeInIl2Cpp]
     public class RecordingRingVisuals : MonoBehaviour
     {
         private Renderer renderer;
         private float pulseProgress = 0f;

         private bool shouldPulse;
         public float frequency = 1.5f;

         void Update()
         {
             pulseProgress += Time.deltaTime;

             // y = 1 - min(mod(x, 1.5), 1)
             // Uses RUMBLE's built in Remap function so it doesnt go fully dark.
             float pulse = (1 - Min(pulseProgress % frequency, 1)).Remap(0, 1, 0.6f, 1);
             Color overlay = new Color(pulse, pulse, pulse, 1);
             renderer.material.SetColor("_Overlay", overlay);
         }

         public void Initialize()
         {
             renderer = GetComponent<Renderer>();
         }

         public void SetPulsing(bool ShouldPulse)
         {
             shouldPulse = ShouldPulse;
             
             if (!ShouldPulse)
                renderer.material.SetColor("_Overlay", new Color(0, 0, 0, 1));
         }
         
         public IEnumerator StopPulse()
         {
             const float epsilon = 0.03f;

             while (true)
             {
                 float remainder = pulseProgress % frequency;
                 if (remainder < epsilon || remainder > frequency - epsilon)
                     break;

                 yield return null;
             }

             shouldPulse = false;
         }
     }
} 