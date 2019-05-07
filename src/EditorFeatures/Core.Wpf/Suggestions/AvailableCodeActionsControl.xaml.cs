using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
using Microsoft.CodeAnalysis.CodeActions;

namespace Microsoft.CodeAnalysis.Editor.Suggestions
{
    /// <summary>
    /// Interaction logic for AvailableCodeActionsControl.xaml
    /// </summary>
    internal partial class AvailableCodeActionsControl : UserControl
    {
        private const int maxNodeTextLength = 20;

        public AvailableCodeActionsControl()
        {
            InitializeComponent();
        }

        public void Refresh(SortedDictionary<int, List<(SyntaxNode, CodeAction)>> availableActions, Dictionary<string, int> codeActionTitleToKeyMap)
        {
            Debug.Assert(availableActions.Count == codeActionTitleToKeyMap.Count);
            Debug.Assert(availableActions.Keys.All(k => codeActionTitleToKeyMap.ContainsValue(k)));

            this.AvailableActionsTreeView.Items.Clear();

            var orderedCodeActionTitleToKeyEntries = codeActionTitleToKeyMap.OrderBy(kvp => kvp.Value);
            var enumerator = orderedCodeActionTitleToKeyEntries.GetEnumerator();
            foreach (var actionsWithSameTitle in availableActions)
            {
                enumerator.MoveNext();
                Debug.Assert(enumerator.Current.Value == actionsWithSameTitle.Key);

                var newChild = new TreeViewItem
                {
                    Header = $"'{enumerator.Current.Key}' ({actionsWithSameTitle.Value.Count})"
                };

                foreach (var (node, codeAction) in actionsWithSameTitle.Value)
                {
                    var nodeText = node.ToString();
                    var indexOfNewLine = nodeText.IndexOf(Environment.NewLine);
                    if (indexOfNewLine < maxNodeTextLength)
                    {
                        nodeText = nodeText.Substring(0, indexOfNewLine);
                    }
                    else if (nodeText.Length > maxNodeTextLength)
                    {
                        nodeText = nodeText.Substring(0, maxNodeTextLength);
                    }

                    var startLinePosition = node.GetLocation().GetLineSpan().StartLinePosition;

                    var newGrandChild = new TreeViewItem
                    {
                        Header = $"{nodeText} ({startLinePosition.Line}, {startLinePosition.Character})"
                    };

                    newChild.Items.Add(newGrandChild);
                }

                this.AvailableActionsTreeView.Items.Add(newChild);
            }
        }
    }
}
