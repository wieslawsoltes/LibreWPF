// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace MS.Internal.Documents
{
    /// <summary>
    /// Portable build implementation used when WinForms-based signature dialogs are unavailable.
    /// </summary>
    internal sealed class DocumentSignatureManager
    {
        private DocumentSignatureManager(IDigitalSignatureProvider digitalSignatureProvider)
        {
            ArgumentNullException.ThrowIfNull(digitalSignatureProvider);
            DigitalSignatureProvider = digitalSignatureProvider;
        }

        public event EventHandler SignaturesChanged;

        public event SignatureStatusChangeHandler SignatureStatusChange;

        internal void Evaluate()
        {
            OnSignatureStatusChange(IsSigned ? SignatureStatus.Undetermined : SignatureStatus.NotSigned);
        }

        internal void VerifySignatures()
        {
            DigitalSignatureProvider.VerifySignatures();
            Evaluate();
        }

        internal void ShowSignatureSummaryDialog()
        {
        }

        internal void ShowSignatureRequestSummaryDialog()
        {
        }

        internal void ShowSigningDialog()
        {
        }

        internal void ShowSigningDialog(IntPtr parentWindow)
        {
        }

        internal IList<SignatureResources> GetSignatureResourceList(bool requestsOnly)
        {
            List<SignatureResources> resources = new List<SignatureResources>();

            foreach (DigitalSignature signature in DigitalSignatureProvider.Signatures)
            {
                bool isRequest = signature.SignatureState == SignatureStatus.NotSigned;
                if (!requestsOnly || isRequest)
                {
                    resources.Add(SignatureResourceHelper.GetResources(signature, CertificatePriorityStatus.Ok));
                }
            }

            return resources;
        }

        internal void OnSign(SignatureResources? signatureResources, IntPtr parentWindow)
        {
        }

        internal void OnCertificateView(SignatureResources signatureResources, IntPtr parentWindow)
        {
        }

        internal void OnSummaryAdd()
        {
        }

        internal void OnSummaryDelete(SignatureResources signatureResources)
        {
            RaiseSignaturesChanged();
        }

        internal void OnAddRequestSignature(SignatureResources sigResources, DateTime dateTime)
        {
            RaiseSignaturesChanged();
        }

        internal bool HasCertificate(SignatureResources signatureResources)
        {
            return false;
        }

        internal static void Initialize(IDigitalSignatureProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (_singleton == null)
            {
                _singleton = new DocumentSignatureManager(provider);
            }
        }

        internal static DocumentSignatureManager Current
        {
            get { return _singleton; }
        }

        internal bool IsSigned
        {
            get { return DigitalSignatureProvider.IsSigned; }
        }

        internal bool IsSignable
        {
            get { return DigitalSignatureProvider.IsSignable; }
        }

        internal bool HasRequests
        {
            get { return DigitalSignatureProvider.HasRequests; }
        }

        internal delegate void SignatureStatusChangeHandler(object sender, SignatureStatusEventArgs args);

        private void OnSignatureStatusChange(SignatureStatus newStatus)
        {
            SignatureStatusEventArgs args = new SignatureStatusEventArgs(
                newStatus,
                SignatureResourceHelper.GetDocumentLevelResources(newStatus));

            SignatureStatusChange?.Invoke(this, args);
        }

        private void RaiseSignaturesChanged()
        {
            SignaturesChanged?.Invoke(this, EventArgs.Empty);
        }

        private IDigitalSignatureProvider DigitalSignatureProvider
        {
            get { return _digitalSignatureProvider; }
            set { _digitalSignatureProvider = value; }
        }

        private static DocumentSignatureManager _singleton;
        private IDigitalSignatureProvider _digitalSignatureProvider;

        public class SignatureStatusEventArgs : EventArgs
        {
            public SignatureStatusEventArgs(
                SignatureStatus signatureStatus,
                DocumentStatusResources statusResources)
            {
                _signatureStatus = signatureStatus;
                _statusResources = statusResources;
            }

            public SignatureStatus SignatureStatus
            {
                get { return _signatureStatus; }
            }

            public DocumentStatusResources StatusResources
            {
                get { return _statusResources; }
            }

            private SignatureStatus _signatureStatus;
            private DocumentStatusResources _statusResources;
        }
    }
}
