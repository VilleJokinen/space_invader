// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using System;
using System.Collections.Generic;

namespace Metaplay.Unity.IAP
{
/// <summary>
/// Helper for tracking the IAP flow states of specific products.
///
/// This is not robust for complex purchase flows, such as ones that span multiple sessions.
/// This should only be used for non-critical things like UI.
/// </summary>
public class IAPFlowTracker
{
    public enum FlowStep
    {
        /// <summary> Pending dynamic-content purchase registered <see cref="IAPManager.RegisterPendingDynamicPurchase"/>, or pending static purchase context registered <see cref="IAPManager.RegisterPendingStaticPurchase"/>. </summary>
        BeganPurchasePreparation,
        /// <summary> Pending dynamic-content purchase (or pending static purchase context) unregistered because session ended. </summary>
        AbortedPurchasePreparation,

        /// <summary> Called to store to start the purchase </summary>
        Initiated,
        /// <summary> Purchase failed in store (e.g. cancelled by user) </summary>
        PurchaseFailed,
        /// <summary> Unexpectedly failed to start validation </summary>
        FailedToStartValidation,
        /// <summary> Validation started (i.e. sent to server) </summary>
        StartedValidation,
        /// <summary> Finished, successful purchase. Claimed and rewards granted to player state. Was (or will soon be) confirmed to the store. </summary>
        FinishedWithSuccessAndClaimed,
        /// <summary> Finished, but it was a duplicate purchase. Cleared from player state, but rewards not granted. Was (or will soon be) confirmed to the store. </summary>
        FinishedWithDuplicateReceipt,
        /// <summary> Finished, but the receipt purchase was invalid. Either due to a cheater or a bug. Not confirmed to the store. </summary>
        FinishedWithInvalidReceipt,
        /// <summary> Finished with unexpected status. Shouldn't happen, it's a bug, but let's tolerate. </summary>
        FinishedWithUnexpectedStatus,
    }

    public struct FlowStepInfo
    {
        public FlowStep                         Step;
        public IAPManager.StorePurchaseFailure? StorePurchaseFailureMaybe; // For FlowStep.PurchaseFailed
        public FlowStepInfo(FlowStep step, IAPManager.StorePurchaseFailure? storePurchaseFailureMaybe)
        {
            Step                        = step;
            StorePurchaseFailureMaybe   = storePurchaseFailureMaybe;
        }
    }

    Dictionary<InAppProductId, FlowStepInfo> _bestEffortLastKnownFlowSteps = new Dictionary<InAppProductId, FlowStepInfo>();

    public FlowStepInfo? GetBestEffortLastKnownFlowStepInfo(InAppProductId productId)
    {
        if (_bestEffortLastKnownFlowSteps.TryGetValue(productId, out FlowStepInfo step))
            return step;
        else
            return null;
    }

    public bool PurchaseFlowIsOngoing(InAppProductId productId)
    {
        FlowStep? step = GetBestEffortLastKnownFlowStepInfo(productId)?.Step;
        return step.HasValue && !step.Value.IsTerminalStep();
    }

    public delegate void OnBestEffortKnownFlowStepHandler(InAppProductId productId, FlowStepInfo info);

    public event OnBestEffortKnownFlowStepHandler OnBestEffortKnownFlowStep;

    IAPManager _iapManager;

    public IAPFlowTracker(IAPManager iapManager)
    {
        _iapManager = iapManager ?? throw new ArgumentNullException(nameof(iapManager));

        _iapManager.OnPendingDynamicPurchaseRegistered    += OnPendingDynamicPurchaseRegistered;
        _iapManager.OnPendingDynamicPurchaseUnregistered  += OnPendingDynamicPurchaseUnregistered;
        _iapManager.OnPendingStaticPurchaseRegistered     += OnPendingStaticPurchaseContextRegistered;
        _iapManager.OnPendingStaticPurchaseUnregistered   += OnPendingStaticPurchaseContextUnregistered;
        _iapManager.OnInitiatingPurchase                  += OnInitiatingPurchase;
        _iapManager.OnStorePurchaseFailed                 += OnStorePurchaseFailed;
        _iapManager.OnFailedToStartPurchaseValidation     += OnFailedToStartPurchaseValidation;
        _iapManager.OnStartedPurchaseValidation           += OnStartedPurchaseValidation;
        _iapManager.OnPurchaseFinishedInMetaplay          += OnPurchaseFinishedInMetaplay;
    }

    public void Dispose()
    {
        _iapManager.OnPendingDynamicPurchaseRegistered    -= OnPendingDynamicPurchaseRegistered;
        _iapManager.OnPendingDynamicPurchaseUnregistered  -= OnPendingDynamicPurchaseUnregistered;
        _iapManager.OnPendingStaticPurchaseRegistered     -= OnPendingStaticPurchaseContextRegistered;
        _iapManager.OnPendingStaticPurchaseUnregistered   -= OnPendingStaticPurchaseContextUnregistered;
        _iapManager.OnInitiatingPurchase                  -= OnInitiatingPurchase;
        _iapManager.OnStorePurchaseFailed                 -= OnStorePurchaseFailed;
        _iapManager.OnFailedToStartPurchaseValidation     -= OnFailedToStartPurchaseValidation;
        _iapManager.OnStartedPurchaseValidation           -= OnStartedPurchaseValidation;
        _iapManager.OnPurchaseFinishedInMetaplay          -= OnPurchaseFinishedInMetaplay;
    }

    #region Events from IAPManager

    void OnPendingDynamicPurchaseRegistered(InAppProductId productId)
    {
        SetFlowStep(productId, FlowStep.BeganPurchasePreparation);
    }

    void OnPendingDynamicPurchaseUnregistered(InAppProductId productId)
    {
        SetFlowStep(productId, FlowStep.AbortedPurchasePreparation);
    }

    void OnPendingStaticPurchaseContextRegistered(InAppProductId productId)
    {
        SetFlowStep(productId, FlowStep.BeganPurchasePreparation);
    }

    void OnPendingStaticPurchaseContextUnregistered(InAppProductId productId)
    {
        SetFlowStep(productId, FlowStep.AbortedPurchasePreparation);
    }

    void OnInitiatingPurchase(InAppProductId productId)
    {
        SetFlowStep(productId, FlowStep.Initiated);
    }

    void OnStorePurchaseFailed(InAppProductId productId, IAPManager.StorePurchaseFailure failure)
    {
        SetFlowStep(productId, FlowStep.PurchaseFailed, failure);
    }

    void OnFailedToStartPurchaseValidation(InAppPurchaseEvent purchaseEvent)
    {
        SetFlowStep(purchaseEvent.ProductId, FlowStep.FailedToStartValidation);
    }

    void OnStartedPurchaseValidation(InAppPurchaseEvent purchaseEvent)
    {
        SetFlowStep(purchaseEvent.ProductId, FlowStep.StartedValidation);
    }

    void OnPurchaseFinishedInMetaplay(InAppPurchaseEvent purchaseEvent)
    {
        if (purchaseEvent.Status == InAppPurchaseStatus.ValidReceipt)
            SetFlowStep(purchaseEvent.ProductId, FlowStep.FinishedWithSuccessAndClaimed);
        else if (purchaseEvent.Status == InAppPurchaseStatus.ReceiptAlreadyUsed)
            SetFlowStep(purchaseEvent.ProductId, FlowStep.FinishedWithDuplicateReceipt);
        else if (purchaseEvent.Status == InAppPurchaseStatus.InvalidReceipt)
            SetFlowStep(purchaseEvent.ProductId, FlowStep.FinishedWithInvalidReceipt);
        else
            SetFlowStep(purchaseEvent.ProductId, FlowStep.FinishedWithUnexpectedStatus);
    }

    #endregion

    void SetFlowStep(InAppProductId productId, FlowStep step, IAPManager.StorePurchaseFailure? storePurchaseFailureMaybe = null)
    {
        FlowStepInfo info = new FlowStepInfo(step, storePurchaseFailureMaybe);
        _bestEffortLastKnownFlowSteps[productId] = info;
        OnBestEffortKnownFlowStep?.Invoke(productId, info);
    }
}

public static class IAPFlowStepExtensions
{
    public static bool IsInitialStep(this IAPFlowTracker.FlowStep step)
    {
        return step == IAPFlowTracker.FlowStep.BeganPurchasePreparation
            || step == IAPFlowTracker.FlowStep.Initiated;
    }

    public static bool IsTerminalStep(this IAPFlowTracker.FlowStep step)
    {
        return step == IAPFlowTracker.FlowStep.AbortedPurchasePreparation
            || step == IAPFlowTracker.FlowStep.PurchaseFailed
            || step == IAPFlowTracker.FlowStep.FailedToStartValidation
            || step == IAPFlowTracker.FlowStep.FinishedWithSuccessAndClaimed
            || step == IAPFlowTracker.FlowStep.FinishedWithDuplicateReceipt
            || step == IAPFlowTracker.FlowStep.FinishedWithInvalidReceipt
            || step == IAPFlowTracker.FlowStep.FinishedWithUnexpectedStatus;
    }
}
}
