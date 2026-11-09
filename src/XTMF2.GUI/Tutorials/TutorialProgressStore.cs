/*
    Copyright 2026 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace XTMF2.GUI.Tutorials;

public sealed class TutorialProgressStore
{
    private readonly HashSet<string> _completedTutorialIds;
    private readonly Action<IReadOnlyCollection<string>> _persist;

    public IReadOnlySet<string> CompletedTutorialIds => _completedTutorialIds;

    public TutorialProgressStore(
        IEnumerable<string> completedTutorialIds,
        Action<IReadOnlyCollection<string>> persist)
    {
        ArgumentNullException.ThrowIfNull(completedTutorialIds);
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _completedTutorialIds = new HashSet<string>(
            completedTutorialIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsCompleted(string tutorialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tutorialId);
        return _completedTutorialIds.Contains(tutorialId);
    }

    public void MarkCompleted(string tutorialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tutorialId);
        if (!_completedTutorialIds.Add(tutorialId))
            return;

        try
        {
            _persist(_completedTutorialIds.ToArray());
        }
        catch
        {
            _completedTutorialIds.Remove(tutorialId);
            throw;
        }
    }
}
