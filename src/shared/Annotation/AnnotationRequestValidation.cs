using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Annotation
{
    internal static class AnnotationRequestValidation
    {
        internal static bool TryValidateTextContent(string text, string fieldName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = fieldName + " must be non-empty text";
                return false;
            }

            return true;
        }

        internal static bool TryValidateLeaderPointCount(int count, out string error)
        {
            error = null;
            if (count < 2)
            {
                error = "points must contain at least 2 point objects";
                return false;
            }

            return true;
        }

        internal static bool TryValidateTableShape(int rows, int columns, JArray cells, out string error)
        {
            error = null;
            if (rows <= 0)
            {
                error = "rows must be a positive integer";
                return false;
            }

            if (columns <= 0)
            {
                error = "columns must be a positive integer";
                return false;
            }

            if (cells == null)
            {
                error = "cells must be an array of row arrays";
                return false;
            }

            if (cells.Count > rows)
            {
                error = "cells row count must fit within rows";
                return false;
            }

            for (var row = 0; row < cells.Count; row++)
            {
                var cellRow = cells[row] as JArray;
                if (cellRow == null)
                {
                    error = "cells[" + row + "] must be an array";
                    return false;
                }

                if (cellRow.Count > columns)
                {
                    error = "cells[" + row + "] column count must fit within columns";
                    return false;
                }
            }

            return true;
        }
    }
}
