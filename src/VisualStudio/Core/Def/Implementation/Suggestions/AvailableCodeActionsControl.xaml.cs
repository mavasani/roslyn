using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Analyzer.Utilities.PooledObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.AvailableCodeActions;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.PlatformUI;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Suggestions
{
    /// <summary>
    /// Interaction logic for AvailableCodeActionsControl.xaml
    /// </summary>
    internal partial class AvailableCodeActionsControl : UserControl
    {
        private const int maxNodeTextLength = 100;
        private IAvailableCodeActionsService _availableCodeActionsService;

        public AvailableCodeActionsControl()
        {
            InitializeComponent();
        }

        public void Initialize(IAvailableCodeActionsService availableCodeActionsService)
        {
            _availableCodeActionsService = availableCodeActionsService;
        }

        public void Refresh(SyntaxNode root, SortedDictionary<int, List<(TextSpan span, CodeAction action)>> availableActions, Dictionary<string, int> codeActionTitleToKeyMap, Action<TextSpan> onGotFocus)
        {
            Debug.Assert(availableActions.Count == codeActionTitleToKeyMap.Count);
            Debug.Assert(availableActions.Keys.All(k => codeActionTitleToKeyMap.ContainsValue(k)));

            this.AvailableActionsTreeView.Items.Clear();

            var orderedCodeActionTitleToKeyEntries = codeActionTitleToKeyMap.OrderBy(kvp => kvp.Value);
            var enumerator = orderedCodeActionTitleToKeyEntries.GetEnumerator();
            var text = root.GetText();
            foreach (var actionsWithSameTitle in availableActions)
            {
                enumerator.MoveNext();
                Debug.Assert(enumerator.Current.Value == actionsWithSameTitle.Key);

                var newChild = new TreeViewItem
                {
                    Header = $"{enumerator.Current.Key} ({actionsWithSameTitle.Value.Count})"
                };

                var processedSpans = PooledHashSet<TextSpan>.GetInstance();
                foreach (var (span, codeAction) in actionsWithSameTitle.Value)
                {
                    if (!processedSpans.Add(span))
                    {
                        continue;
                    }

                    var nodeText = span.IsEmpty
                        ? text.ToString(root.FindToken(span.Start).Span)
                        : text.ToString(span);
                    var indexOfNewLine = nodeText.IndexOf(Environment.NewLine);
                    if (indexOfNewLine > 0 && indexOfNewLine < maxNodeTextLength)
                    {
                        nodeText = nodeText.Substring(0, indexOfNewLine);
                    }
                    else if (nodeText.Length > maxNodeTextLength)
                    {
                        nodeText = nodeText.Substring(0, maxNodeTextLength);
                    }

                    var startLinePosition = root.SyntaxTree.GetLineSpan(span).StartLinePosition;
                    var fileName = PathUtilities.GetFileName(root.SyntaxTree.FilePath);
                    var newGrandChild = new TreeViewItem
                    {
                        Header = $"{nodeText}     (Line: {startLinePosition.Line + 1})"
                    };

                    newGrandChild.Selected += (sender, args) => onGotFocus(span);

                    newChild.Items.Add(newGrandChild);
                }

                this.AvailableActionsTreeView.Items.Add(newChild);
                this.TitleLabel.Content = $"Actions for '{PathUtilities.GetFileName(root.SyntaxTree.FilePath)}':";

                processedSpans.Free();
            }
        }

        private void RefreshActions_Click(object sender, RoutedEventArgs e)
        {
            using var cts = new CancellationTokenSource();
            _availableCodeActionsService.UpdateAvailableCodeActionsForActiveDocumentAsync(cts.Token).Wait(cts.Token);
        }
    }
}
