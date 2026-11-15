using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace Catch2OpenFolder
{
    // Only tags tests returned by opted-in executable discovery. The built-in
    // TestStatus provider owns results, execution, and native debugger launch.
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("C/C++")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TagType(typeof(ICodeLensTag))]
    internal sealed class Catch2CodeLensTaggerProvider : IViewTaggerProvider
    {
        [Import] internal ITextDocumentFactoryService Documents = null;
        [Import] internal WorkspaceTestContainerDiscoverer Discoverer = null;

        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (buffer != textView.TextBuffer || !(textView is IWpfTextView view) ||
                !Documents.TryGetTextDocument(buffer, out var document)) return null;
            return new Catch2CodeLensTagger(view, document, Discoverer) as ITagger<T>;
        }
    }

    internal sealed class Catch2CodeLensTagger : ITagger<ICodeLensTag>, IDisposable
    {
        private readonly IWpfTextView view;
        private readonly ITextDocument document;
        private readonly WorkspaceTestContainerDiscoverer discoverer;
        private readonly List<Catch2CodeLensTag> tags = new List<Catch2CodeLensTag>();
        private bool disposed;
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        internal Catch2CodeLensTagger(IWpfTextView view, ITextDocument document,
            WorkspaceTestContainerDiscoverer discoverer)
        {
            this.view = view;
            this.document = document;
            this.discoverer = discoverer;
            discoverer.CodeLensTestsUpdated += IndexChanged;
            view.TextBuffer.Changed += BufferChanged;
            view.Closed += ViewClosed;
            RebuildTags();
        }

        private void IndexChanged(object sender, EventArgs args)
        {
            view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!disposed) RebuildTags();
            }));
        }

        private void RebuildTags()
        {
            foreach (var tag in tags) tag.Disconnect();
            tags.Clear();
            var snapshot = view.TextSnapshot;
            var sourceText = snapshot.GetText();
            foreach (var test in discoverer.CodeLensTests)
            {
                if (test.Line <= 0 || test.Line > snapshot.LineCount ||
                    !string.Equals(test.Filename, document.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                var anchor = TestDeclarationLocator.FindAnchor(sourceText, test.Line);
                if (anchor < 0) continue;
                tags.Add(new Catch2CodeLensTag(document.FilePath, test.Name, test.Source,
                    snapshot.CreateTrackingSpan(anchor, 1, SpanTrackingMode.EdgeExclusive), view.TextBuffer));
            }
            NotifyChanged();
        }

        private void BufferChanged(object sender, TextContentChangedEventArgs args) => NotifyChanged();
        private void NotifyChanged()
        {
            if (!disposed) TagsChanged?.Invoke(this,
                new SnapshotSpanEventArgs(new SnapshotSpan(view.TextSnapshot, 0, view.TextSnapshot.Length)));
        }

        public IEnumerable<ITagSpan<ICodeLensTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (disposed || spans.Count == 0) yield break;
            foreach (var tag in tags)
            {
                var span = tag.TrackingSpan.GetSpan(spans[0].Snapshot);
                if (spans.IntersectsWith(span)) yield return new TagSpan<ICodeLensTag>(span, tag);
            }
        }

        private void ViewClosed(object sender, EventArgs args) => Dispose();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            discoverer.CodeLensTestsUpdated -= IndexChanged;
            view.TextBuffer.Changed -= BufferChanged;
            view.Closed -= ViewClosed;
            foreach (var tag in tags) tag.Disconnect();
            tags.Clear();
        }
    }

    internal sealed class Catch2CodeLensTag : ICodeLensTag3, ICodeLensDescriptor,
        ICodeLensDescriptorContextProvider
    {
        private readonly string source;
        private readonly ITextBuffer buffer;
        internal ITrackingSpan TrackingSpan { get; }
        public string FilePath { get; }
        public Guid ProjectGuid => Guid.Empty;
        public string ElementDescription { get; }
        public Span? ApplicableSpan => TrackingSpan.GetSpan(buffer.CurrentSnapshot).Span;
        public CodeElementKinds Kind => CodeElementKinds.Function;
        public ICodeLensDescriptor Descriptor => this;
        public ICodeLensDescriptorContextProvider DescriptorContextProvider => this;
        public CodeLensTagProperties Properties { get; } = new CodeLensTagProperties(false);
        public event EventHandler Disconnected;

        internal Catch2CodeLensTag(string file, string name, string source,
            ITrackingSpan trackingSpan, ITextBuffer buffer)
        {
            FilePath = file;
            ElementDescription = name;
            this.source = source;
            TrackingSpan = trackingSpan;
            this.buffer = buffer;
        }

        public Task<CodeLensDescriptorContext> GetCurrentContextAsync()
        {
            return Task.FromResult(new CodeLensDescriptorContext(ApplicableSpan,
                new Dictionary<object, object>
                {
                    { "OutputFilePath", source },
                    { "FullyQualifiedName", ElementDescription },
                    { "VisualStudioProcessId", Process.GetCurrentProcess().Id }
                }));
        }
        internal void Disconnect() => Disconnected?.Invoke(this, EventArgs.Empty);
    }
}
