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
        private readonly Func<ReportDocument, bool> _isDocumentOpen;
        private bool _isLive;

        public ReportLinkViewModel(ReportDocument document, ReportLink link, INavigationService navigation,
            Func<ReportDocument, bool> isDocumentOpen)
        {
            _document = document;
            _link = link ?? throw new ArgumentNullException(nameof(link));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _isDocumentOpen = isDocumentOpen ?? throw new ArgumentNullException(nameof(isDocumentOpen));
            _isLive = _isDocumentOpen(_document);
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
            if (SetProperty(ref _isLive, _isDocumentOpen(_document), nameof(IsLive)))
                OpenCommand.RaiseCanExecuteChanged();
        }

        private void Open()
        {
            if (!_isDocumentOpen(_document)) return;
            _navigation.Navigate(new ReportNavigationRequest
            {
                Document = _document, Entities = _link.Entities ?? new List<ReportEntity>()
            });
        }
    }
}
