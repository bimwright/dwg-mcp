using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin.Presentation;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Presentation
{
    public sealed class ReportLinkViewModel : ObservableObject
    {
        private readonly ReportDocument _document;
        private readonly ReportLink _link;
        private readonly INavigationService _navigation;
        private readonly Func<ActiveDocumentIdentity> _activeDocument;
        private bool _isLive;

        public ReportLinkViewModel(ReportDocument document, ReportLink link, INavigationService navigation,
            Func<ActiveDocumentIdentity> activeDocument)
        {
            _document = document;
            _link = link ?? throw new ArgumentNullException(nameof(link));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
            _isLive = ReportDocumentMatch.Matches(_document, _activeDocument());
            OpenCommand = new RelayCommand(Open, () => IsLive);
        }

        public string Label => _link.Label;
        public bool IsLive => _isLive;
        public string DimTooltip => _document?.Title ?? "";
        public int EntityCount => _link.Entities?.Count ?? 0;
        public IReadOnlyList<ReportEntity> Entities => _link.Entities ?? (IReadOnlyList<ReportEntity>)new ReportEntity[0];
        public RelayCommand OpenCommand { get; }

        public void UpdateActiveDocument()
        {
            if (SetProperty(ref _isLive, ReportDocumentMatch.Matches(_document, _activeDocument()), nameof(IsLive)))
                OpenCommand.RaiseCanExecuteChanged();
        }

        private void Open()
        {
            if (!ReportDocumentMatch.Matches(_document, _activeDocument())) return;
            _navigation.Navigate(new ReportNavigationRequest
            {
                Document = _document, Entities = _link.Entities ?? new List<ReportEntity>()
            });
        }
    }
}
