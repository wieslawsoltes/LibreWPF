// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Security.RightsManagement;

namespace MS.Internal.Documents
{
    /// <summary>
    /// Portable build implementation used when WinForms-based rights management UI is unavailable.
    /// </summary>
    internal sealed class DocumentRightsManagementManager
    {
        private DocumentRightsManagementManager(IRightsManagementProvider rmProvider)
        {
            ArgumentNullException.ThrowIfNull(rmProvider);
            _rmProviderCache = rmProvider;
        }

        internal static void Initialize(IRightsManagementProvider rmProvider)
        {
            ArgumentNullException.ThrowIfNull(rmProvider);

            if (_currentManager == null)
            {
                _currentManager = new DocumentRightsManagementManager(rmProvider);
            }
        }

        internal Stream DecryptPackage()
        {
            return null;
        }

        internal void Evaluate()
        {
            RightsManagementStatus status = _rmProvider.IsProtected
                ? RightsManagementStatus.Protected
                : RightsManagementStatus.Unprotected;

            RightsManagementPolicy policy = _rmProvider.IsProtected
                ? RightsManagementPolicy.AllowNothing
                : RightsManagementPolicy.AllowView
                    | RightsManagementPolicy.AllowPrint
                    | RightsManagementPolicy.AllowCopy
                    | RightsManagementPolicy.AllowSign
                    | RightsManagementPolicy.AllowAnnotate;

            OnRMStatusChange(status);
            OnRMPolicyChange(policy);
        }

        internal void SetEncryptedPackage(EncryptedPackageEnvelope newPackage)
        {
            Evaluate();
        }

        internal void ShowCredentialManagementUI()
        {
        }

        internal void ShowEnrollment()
        {
        }

        internal bool Enroll(EnrollmentAccountType accountType)
        {
            return false;
        }

        internal void ShowPermissions()
        {
        }

        internal void ShowPublishing()
        {
        }

        internal void OnCredentialManagementSetDefault(string defaultAccount)
        {
        }

        internal void OnCredentialManagementRemove(string accountName)
        {
        }

        internal void OnCredentialManagementShowEnrollment()
        {
        }

        internal void PromptToInstallRM()
        {
        }

        internal IList<string> GetCredentialManagementResourceList()
        {
            return new List<string>();
        }

        internal string GetDefaultCredentialManagementResource()
        {
            return string.Empty;
        }

        internal static DocumentRightsManagementManager Current
        {
            get { return _currentManager; }
        }

        internal PublishLicense PublishLicense
        {
            set
            {
                if (_rmProvider.CurrentPublishLicense != value)
                {
                    _rmProvider.CurrentPublishLicense = value;
                    OnPublishLicenseChange();
                }
            }
        }

        internal bool HasPermissionToSave
        {
            get
            {
                return !_rmProvider.IsProtected
                    || (_rmProvider.CurrentUseLicense?.HasPermission(RightsManagementPermissions.AllowCopy) == true);
            }
        }

        internal bool HasPermissionToEdit
        {
            get
            {
                return !_rmProvider.IsProtected
                    || (_rmProvider.CurrentUseLicense?.HasPermission(RightsManagementPermissions.AllowEdit) == true);
            }
        }

        internal bool IsRMInstalled
        {
            get { return false; }
        }

        internal event RMStatusChangeHandler RMStatusChange;

        internal event EventHandler PublishLicenseChange;

        internal event RMPolicyChangeHandler RMPolicyChange;

        internal delegate void RMStatusChangeHandler(object sender, RightsManagementStatusEventArgs args);

        internal delegate void RMPolicyChangeHandler(object sender, RightsManagementPolicyEventArgs args);

        private void OnRMStatusChange(RightsManagementStatus newStatus)
        {
            RMStatusChange?.Invoke(this, new RightsManagementStatusEventArgs(newStatus));
        }

        private void OnRMPolicyChange(RightsManagementPolicy newPolicy)
        {
            RMPolicyChange?.Invoke(this, new RightsManagementPolicyEventArgs(newPolicy));
        }

        private void OnPublishLicenseChange()
        {
            PublishLicenseChange?.Invoke(this, EventArgs.Empty);
        }

        private IRightsManagementProvider _rmProvider
        {
            get { return _rmProviderCache; }
        }

        private static DocumentRightsManagementManager _currentManager;
        private IRightsManagementProvider _rmProviderCache;

        public class RightsManagementStatusEventArgs : EventArgs
        {
            public RightsManagementStatusEventArgs(RightsManagementStatus rmStatus)
            {
                _rmStatus = rmStatus;
            }

            public RightsManagementStatus RMStatus
            {
                get { return _rmStatus; }
            }

            public DocumentStatusResources StatusResources
            {
                get
                {
                    if (!_statusResourcesLoaded)
                    {
                        _statusResources = RightsManagementResourceHelper.GetDocumentLevelResources(_rmStatus);
                        _statusResourcesLoaded = true;
                    }

                    return _statusResources;
                }
            }

            private RightsManagementStatus _rmStatus;
            private DocumentStatusResources _statusResources;
            private bool _statusResourcesLoaded;
        }

        public class RightsManagementPolicyEventArgs : EventArgs
        {
            internal RightsManagementPolicyEventArgs(RightsManagementPolicy rmPolicy)
            {
                _rmPolicy = rmPolicy;
            }

            public RightsManagementPolicy RMPolicy
            {
                get { return _rmPolicy; }
            }

            private RightsManagementPolicy _rmPolicy;
        }
    }
}
