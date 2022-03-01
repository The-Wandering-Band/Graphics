using System.Collections.Generic;
using System.Linq;
using UnityEngine.GraphToolsFoundation.CommandStateObserver;
using UnityEngine;

namespace UnityEditor.GraphToolsFoundation.Overdrive
{
    /// <summary>
    /// A command to reset the color of some graph element models
    /// </summary>
    public class ResetElementColorCommand : ModelCommand<IGraphElementModel>
    {
        const string k_UndoStringSingular = "Reset Element Color";
        const string k_UndoStringPlural = "Reset Elements Color";

        /// <summary>
        /// Initializes a new instance of the <see cref="ResetElementColorCommand" /> class.
        /// </summary>
        public ResetElementColorCommand()
            : base(k_UndoStringSingular)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResetElementColorCommand" /> class.
        /// </summary>
        /// <param name="models">Element models to reset</param>
        public ResetElementColorCommand(IReadOnlyList<IGraphElementModel> models)
            : base(k_UndoStringSingular, k_UndoStringPlural, models)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResetElementColorCommand" /> class.
        /// </summary>
        /// <param name="models">Element models to reset</param>
        public ResetElementColorCommand(params IGraphElementModel[] models)
            : this((IReadOnlyList<IGraphElementModel>)models)
        {
        }

        /// <summary>
        /// Default command handler
        /// </summary>
        /// <param name="undoState">The undo state component.</param>
        /// <param name="graphViewState">The graph view state component.</param>
        /// <param name="command">The command to apply to the state.</param>
        public static void DefaultCommandHandler(UndoStateComponent undoState, GraphViewStateComponent graphViewState, ResetElementColorCommand command)
        {
            if (command.Models != null)
            {
                using (var undoStateUpdater = undoState.UpdateScope)
                {
                    undoStateUpdater.SaveSingleState(graphViewState, command);
                }

                using (var updater = graphViewState.UpdateScope)
                {
                    foreach (var model in command.Models.Where(c => c.IsColorable()))
                    {
                        model.ResetColor();
                    }

                    updater.MarkChanged(command.Models);
                }
            }
        }
    }

    /// <summary>
    /// A command to change the color of some graph element models
    /// </summary>
    public class ChangeElementColorCommand : ModelCommand<IGraphElementModel, Color>
    {
        const string k_UndoStringSingular = "Change Element Color";
        const string k_UndoStringPlural = "Change Elements Color";

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeElementColorCommand" /> class.
        /// </summary>
        public ChangeElementColorCommand()
            : base(k_UndoStringSingular)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeElementColorCommand" /> class.
        /// </summary>
        /// <param name="color">The color to set</param>
        /// <param name="elementModels">Element models to affect</param>
        public ChangeElementColorCommand(Color color, IReadOnlyList<IGraphElementModel> elementModels)
            : base(k_UndoStringSingular, k_UndoStringPlural, color, elementModels)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeElementColorCommand" /> class.
        /// </summary>
        /// <param name="color">The color to set</param>
        /// <param name="elementModels">Element models to affect</param>
        public ChangeElementColorCommand(Color color, params IGraphElementModel[] elementModels)
            : this(color, (IReadOnlyList<IGraphElementModel>)elementModels)
        {
        }

        /// <summary>
        /// Default command handler
        /// </summary>
        /// <param name="undoState">The undo state component.</param>
        /// <param name="graphViewState">The graph view state component.</param>
        /// <param name="command">The command to apply to the state.</param>
        public static void DefaultCommandHandler(UndoStateComponent undoState, GraphViewStateComponent graphViewState, ChangeElementColorCommand command)
        {
            using (var undoStateUpdater = undoState.UpdateScope)
            {
                undoStateUpdater.SaveSingleState(graphViewState, command);
            }

            if (command.Models != null)
            {
                using (var updater = graphViewState.UpdateScope)
                {
                    foreach (var model in command.Models.Where(c => c.IsColorable()))
                    {
                        model.Color = command.Value;
                    }

                    updater.MarkChanged(command.Models);
                }
            }
        }
    }

    /// <summary>
    /// Command to align nodes hierarchies in a graph view.
    /// </summary>
    public class AlignNodesCommand : UndoableCommand
    {
        /// <summary>
        /// The GraphView in charge of aligning the nodes.
        /// </summary>
        public readonly GraphView GraphView;
        /// <summary>
        /// A list of nodes to align.
        /// </summary>
        public readonly IReadOnlyList<IGraphElementModel> Nodes;
        /// <summary>
        /// True if hierarchies should be aligned. Otherwise, only the nodes in <cref name="Nodes"/> are aligned.
        /// </summary>
        public readonly bool Follow;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public AlignNodesCommand()
        {
            UndoString = "Align Items";
        }

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="graphView">The GraphView in charge of aligning the nodes.</param>
        /// <param name="follow">True if hierarchies should be aligned. Otherwise, only the nodes in <paramref name="nodes"/> are aligned.</param>
        /// <param name="nodes">A list of nodes to align.</param>
        public AlignNodesCommand(GraphView graphView, bool follow, IReadOnlyList<IGraphElementModel> nodes) : this()
        {
            GraphView = graphView;
            Nodes = nodes;
            Follow = follow;

            if (follow)
            {
                UndoString = "Align Hierarchies";
            }
        }

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="graphView">The GraphView in charge of aligning the nodes.</param>
        /// <param name="nodes">A list of nodes to align.</param>
        /// <param name="follow">True if hierarchies should be aligned. Otherwise, only the nodes in <paramref name="nodes"/> are aligned.</param>
        public AlignNodesCommand(GraphView graphView, bool follow, params IGraphElementModel[] nodes)
            : this(graphView, follow, (IReadOnlyList<IGraphElementModel>)nodes)
        {
        }

        /// <summary>
        /// Default handler.
        /// </summary>
        /// <param name="undoState">The undo state component.</param>
        /// <param name="graphViewState">The graph view state component.</param>
        /// <param name="command">The command to apply to the state.</param>
        public static void DefaultCommandHandler(UndoStateComponent undoState, GraphViewStateComponent graphViewState, AlignNodesCommand command)
        {
            if (command.Nodes.Any())
            {
                using (var undoStateUpdater = undoState.UpdateScope)
                {
                    undoStateUpdater.SaveSingleState(graphViewState, command);
                }

                using (var stateUpdater = graphViewState.UpdateScope)
                {
                    command.GraphView.PositionDependenciesManager.AlignNodes(command.Follow, command.Nodes, stateUpdater);
                    stateUpdater.ForceCompleteUpdate();
                }
            }
        }
    }

    /// <summary>
    /// A command to select a graph element models.
    /// </summary>
    public class SelectElementsCommand : ModelCommand<IGraphElementModel>
    {
        /// <summary>
        /// Selection mode.
        /// </summary>
        public enum SelectionMode
        {
            /// <summary>
            /// Replace the selection.
            /// </summary>
            Replace,
            /// <summary>
            /// Add to the selection.
            /// </summary>
            Add,
            /// <summary>
            /// Remove from the selection.
            /// </summary>
            Remove,
            /// <summary>
            /// If the element is not currently selected,
            /// add it to the selection. Otherwise remove it from the selection.
            /// </summary>
            Toggle,
        }

        const string k_UndoStringSingular = "Select Element";
        const string k_UndoStringPlural = "Select Elements";

        /// <summary>
        /// The selection mode.
        /// </summary>
        public SelectionMode Mode;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectElementsCommand" /> class.
        /// </summary>
        public SelectElementsCommand()
            : base(k_UndoStringSingular) {}

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectElementsCommand" /> class.
        /// </summary>
        /// <param name="mode">How should the selection should be modified.</param>
        /// <param name="models">The list of models affected by this command.</param>
        public SelectElementsCommand(SelectionMode mode, IReadOnlyList<IGraphElementModel> models)
            : base(k_UndoStringSingular, k_UndoStringPlural, models)
        {
            Mode = mode;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectElementsCommand" /> class.
        /// </summary>
        /// <param name="mode">How should the selection should be modified.</param>
        /// <param name="models">The list of models affected by this command.</param>
        public SelectElementsCommand(SelectionMode mode, params IGraphElementModel[] models)
            : this(mode, (IReadOnlyList<IGraphElementModel>)models)
        {
        }

        /// <summary>
        /// Default command handler.
        /// </summary>
        /// <param name="undoState">The undo state component.</param>
        /// <param name="graphViewState">The graph view state component.</param>
        /// <param name="selectionState">The selection state component.</param>
        /// <param name="command">The command to apply to the state.</param>
        public static void DefaultCommandHandler(UndoStateComponent undoState, GraphViewStateComponent graphViewState, SelectionStateComponent selectionState, SelectElementsCommand command)
        {
            switch (command.Mode)
            {
                case SelectionMode.Replace:
                    var currentSelection = selectionState.GetSelection(graphViewState.GraphModel);
                    if (currentSelection.SequenceEqual(command.Models))
                        return;
                    break;
                case SelectionMode.Add when command.Models.Count == 0:
                case SelectionMode.Remove when command.Models.Count == 0:
                case SelectionMode.Toggle when command.Models.Count == 0:
                    return;
            }

            using (var undoStateUpdater = undoState.UpdateScope)
            {
                undoStateUpdater.SaveSingleState(selectionState, command);
            }

            using (var selectionUpdater = selectionState.UpdateScope)
            {
                switch (command.Mode)
                {
                    case SelectionMode.Replace:
                        selectionUpdater.ClearSelection(graphViewState.GraphModel);
                        selectionUpdater.SelectElements(command.Models, true);
                        break;
                    case SelectionMode.Add:
                        selectionUpdater.SelectElements(command.Models, true);
                        break;
                    case SelectionMode.Remove:
                        selectionUpdater.SelectElements(command.Models, false);
                        break;
                    case SelectionMode.Toggle:
                        var toSelect = command.Models.Where(m => !selectionState.IsSelected(m)).ToList();
                        selectionUpdater.SelectElements(command.Models, false);
                        selectionUpdater.SelectElements(toSelect, true);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// A command to clear the selection.
    /// </summary>
    public class ClearSelectionCommand : UndoableCommand
    {
        const string k_UndoStringSingular = "Clear Selection";

        /// <summary>
        /// Initializes a new instance of the <see cref="ClearSelectionCommand" /> class.
        /// </summary>
        public ClearSelectionCommand()
        {
            UndoString = k_UndoStringSingular;
        }

        /// <summary>
        /// Default command handler.
        /// </summary>
        /// <param name="undoState">The undo state component.</param>
        /// <param name="graphViewState">The graph view state component.</param>
        /// <param name="selectionState">The selection state component.</param>
        /// <param name="command">The command to apply to the state.</param>
        public static void DefaultCommandHandler(UndoStateComponent undoState, GraphViewStateComponent graphViewState, SelectionStateComponent selectionState, ClearSelectionCommand command)
        {
            var currentSelection = selectionState.GetSelection(graphViewState.GraphModel);
            if (currentSelection.Count == 0)
                return;

            using (var undoStateUpdater = undoState.UpdateScope)
            {
                undoStateUpdater.SaveSingleState(selectionState, command);
            }

            using (var selectionUpdater = selectionState.UpdateScope)
            {
                selectionUpdater.ClearSelection(graphViewState.GraphModel);
            }
        }
    }

    /// <summary>
    /// A command to change the position and size of an element.
    /// </summary>
    public class ChangeElementLayoutCommand : UndoableCommand
    {
        const string k_UndoStringSingular = "Resize Element";

        /// <summary>
        /// The model to resize.
        /// </summary>
        public IResizable Model;
        /// <summary>
        /// The new layout.
        /// </summary>
        public Rect Layout;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeElementLayoutCommand" /> class.
        /// </summary>
        public ChangeElementLayoutCommand()
        {
            UndoString = k_UndoStringSingular;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeElementLayoutCommand" /> class.
        /// </summary>
        /// <param name="resizableModel">The model to resize.</param>
        /// <param name="newLayout">The new position and size.</param>
        public ChangeElementLayoutCommand(IResizable resizableModel, Rect newLayout)
            : this()
        {
            Model = resizableModel;
            Layout = newLayout;
        }

        /// <summary>
        /// Default command handler for ChangeElementLayoutCommand.
        /// </summary>
        /// <param name="undoState">The undo state component.</param>
        /// <param name="graphViewState">The graph view state component.</param>
        /// <param name="command">The command.</param>
        public static void DefaultCommandHandler(UndoStateComponent undoState, GraphViewStateComponent graphViewState, ChangeElementLayoutCommand command)
        {
            if (command.Model.PositionAndSize == command.Layout)
                return;

            using (var undoStateUpdater = undoState.UpdateScope)
            {
                undoStateUpdater.SaveSingleState(graphViewState, command);
            }

            using (var graphUpdater = graphViewState.UpdateScope)
            {
                command.Model.PositionAndSize = command.Layout;
                graphUpdater.MarkChanged(command.Model as IGraphElementModel);
            }
        }
    }
}
