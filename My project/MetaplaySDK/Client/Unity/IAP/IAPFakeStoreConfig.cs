// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Collections.Generic;
using UnityEngine;
using System;
using Metaplay.Core.InAppPurchase;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Metaplay.Unity.IAP
{
    [CreateAssetMenu(fileName = "IAPFakeStoreConfig", menuName = "Metaplay/IAPFakeStoreConfig")]
    public class IAPFakeStoreConfig : ScriptableObject
    {
        static IAPFakeStoreConfig s_instance = null;

        public static IAPFakeStoreConfig Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = Resources.Load<IAPFakeStoreConfig>("IAPFakeStoreConfig");
                    if (s_instance == null)
                    {
                        MetaplaySDK.Logs.IAPFakeStore.Info("Unable to load Resources/IAPFakeStoreConfig; using default-constructed config.");
                        s_instance = CreateInstance<IAPFakeStoreConfig>();
                    }
                }

                return s_instance;
            }
        }

        [Header("General")]
        public string           LocalizedPriceStringFormat          = "${0:0.00}";

        [Header("Initialization")]
        public bool             InitIsSynchronous                   = false;
        [Min(0f)]
        public float            AsyncInitDelay                      = 5f;
        public bool             ForceInitFailure                    = false;
        public List<string>     DisabledProductIds                  = new List<string>();

        [Header("Purchase")]
        public bool             PurchaseIsSynchronous               = false;
        [Min(0f)]
        public float            AsyncPurchaseDelay                  = 1f;
        public bool             ForcePurchaseFailure                = false;
        public bool             UseFixedTransactionId               = false;
        public string           FixedTransactionId                  = "fakeTxnFixed0";
        public bool             ForceIllFormedReceipt               = false;
        public bool             ForceInvalidSignature               = false;
        [Min(0f)]
        public float            ValidationDelay                     = 1f;
        [Range(0f, 1f)]
        public float            ValidationTransientErrorProbability = 0f;
        public bool             PretendSubscriptionIsFamilyShared   = false;
        public PaymentTypeEnum  PaymentType                         = PaymentTypeEnum.Unknown;
        public bool             IgnorePurchaseConfirmation          = false;

        // \note Distinct type from InAppPurchasePaymentType, because this has Unknown instead
        //       of using a nullable enum, because default Unity inspector doesn't deal with nullable.
        public enum PaymentTypeEnum
        {
            Unknown,
            Normal,
            Sandbox,
        }

        public InAppPurchasePaymentType? IAPPaymentType
        {
            get
            {
                switch (PaymentType)
                {
                    case PaymentTypeEnum.Unknown:       return null;
                    case PaymentTypeEnum.Normal:        return InAppPurchasePaymentType.Normal;
                    case PaymentTypeEnum.Sandbox:       return InAppPurchasePaymentType.Sandbox;
                    default:
                        throw new InvalidOperationException($"Unhandled {nameof(PaymentType)} value {PaymentType}");
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(IAPFakeStoreConfig))]
    public class FakeStoreEditor : Editor
    {
        static readonly bool _useFakeUnityPurchasing =
#if METAPLAY_USE_FAKE_UNITY_PURCHASING
            true
#else
            false
#endif
            ;

        public override void OnInspectorGUI()
        {
            if (!_useFakeUnityPurchasing)
            {
                EditorGUILayout.HelpBox("The fake store is only enabled when METAPLAY_USE_FAKE_UNITY_PURCHASING is defined. It is only intended for early development testing, before the real Unity Purchasing has been installed in the project.", MessageType.Error);
                return;
            }

            DrawDefaultInspector();

            if (GUILayout.Button("Forget pending purchases"))
                WithStoreInstance(fakeStore => fakeStore.ForgetPendingPurchases());

            if (GUILayout.Button("Re-request pending processing"))
                WithStoreInstance(fakeStore => fakeStore.ReRequestPendingProcessing());

            if (GUILayout.Button("Forget subscriptions"))
                WithStoreInstance(fakeStore => fakeStore.ForgetSubscriptions());

            if (GUILayout.Button("Restore purchases"))
                WithStoreInstance(fakeStore => fakeStore.RestorePurchases());

            GUILayout.Label("");
            GUILayout.Label("Products", EditorStyles.boldLabel);
            if (FakeStore.InstanceForEditor != null)
                FakeStore.InstanceForEditor.ProductsGUI();
            else
                GUILayout.Label("Available only at runtime");
        }

        void WithStoreInstance(Action<FakeStore> action)
        {
            FakeStore fakeStore = FakeStore.InstanceForEditor;

            if (fakeStore == null)
                throw new InvalidOperationException($"This action is only available at runtime, when a {nameof(FakeStore)} instance exists");

            action(fakeStore);
        }
    }
#endif
}
