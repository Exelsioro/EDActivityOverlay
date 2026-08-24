using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace EDActivityOverlay.Utils
{
    /// <summary>
    /// Utility class providing helper methods to create consistently styled UI elements
    /// </summary>
    public static class UIHelpers
    {
        #region TextBlock Creation Methods
        
        /// <summary>
        /// Creates a TextBlock with the specified text and style key
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="styleKey">The style key from the resource dictionary</param>
        /// <returns>A styled TextBlock</returns>
        public static TextBlock CreateStyledTextBlock(string text, string styleKey)
        {
            return new TextBlock
            {
                Text = text,
                Style = (Style)Application.Current.FindResource(styleKey)
            };
        }

        /// <summary>
        /// Creates a header TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The header text</param>
        /// <returns>A styled header TextBlock</returns>
        public static TextBlock CreateHeaderText(string text)
        {
            return CreateStyledTextBlock(text, "HeaderTextStyle");
        }

        /// <summary>
        /// Creates a sub-header TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The sub-header text</param>
        /// <returns>A styled sub-header TextBlock</returns>
        public static TextBlock CreateSubHeaderText(string text)
        {
            return CreateStyledTextBlock(text, "SubHeaderTextStyle");
        }

        /// <summary>
        /// Creates a body TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The body text</param>
        /// <returns>A styled body TextBlock</returns>
        public static TextBlock CreateBodyText(string text)
        {
            return CreateStyledTextBlock(text, "BodyTextStyle");
        }

        /// <summary>
        /// Creates a caption TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The caption text</param>
        /// <returns>A styled caption TextBlock</returns>
        public static TextBlock CreateCaptionText(string text)
        {
            return CreateStyledTextBlock(text, "CaptionTextStyle");
        }

        /// <summary>
        /// Creates a success TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The success text</param>
        /// <returns>A styled success TextBlock</returns>
        public static TextBlock CreateSuccessText(string text)
        {
            return CreateStyledTextBlock(text, "SuccessTextStyle");
        }

        /// <summary>
        /// Creates an error TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The error text</param>
        /// <returns>A styled error TextBlock</returns>
        public static TextBlock CreateErrorText(string text)
        {
            return CreateStyledTextBlock(text, "ErrorTextStyle");
        }

        /// <summary>
        /// Creates a warning TextBlock with consistent styling
        /// </summary>
        /// <param name="text">The warning text</param>
        /// <returns>A styled warning TextBlock</returns>
        public static TextBlock CreateWarningText(string text)
        {
            return CreateStyledTextBlock(text, "WarningTextStyle");
        }

        #endregion

        #region Border Creation Methods
        
        /// <summary>
        /// Creates a Border with the specified child element and style key
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <param name="styleKey">The style key from the resource dictionary</param>
        /// <returns>A styled Border</returns>
        public static Border CreateStyledBorder(UIElement child, string styleKey)
        {
            return new Border
            {
                Child = child,
                Style = (Style)Application.Current.FindResource(styleKey)
            };
        }

        /// <summary>
        /// Creates a card Border with consistent styling
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled card Border</returns>
        public static Border CreateCardBorder(UIElement child)
        {
            return CreateStyledBorder(child, "CardBorderStyle");
        }

        /// <summary>
        /// Creates a content Border with consistent styling
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled content Border</returns>
        public static Border CreateContentBorder(UIElement child)
        {
            return CreateStyledBorder(child, "ContentBorderStyle");
        }

        /// <summary>
        /// Creates a highlight Border with consistent styling
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled highlight Border</returns>
        public static Border CreateHighlightBorder(UIElement child)
        {
            return CreateStyledBorder(child, "HighlightBorderStyle");
        }

        /// <summary>
        /// Creates a section Border with consistent styling
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled section Border</returns>
        public static Border CreateSectionBorder(UIElement child)
        {
            return CreateStyledBorder(child, "SectionBorderStyle");
        }

        /// <summary>
        /// Creates a window Border with consistent styling
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled window Border</returns>
        public static Border CreateWindowBorder(UIElement child)
        {
            return CreateStyledBorder(child, "WindowBorderStyle");
        }

        /// <summary>
        /// Creates an inline Border with consistent styling
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled inline Border</returns>
        public static Border CreateInlineBorder(UIElement child)
        {
            return CreateStyledBorder(child, "InlineBorderStyle");
        }

        #endregion

        #region Utility Methods
        
        /// <summary>
        /// Creates a spacer Border with specified height
        /// </summary>
        /// <param name="height">The height of the spacer</param>
        /// <returns>A transparent spacer Border</returns>
        public static Border CreateSpacer(double height)
        {
            return new Border
            {
                Height = height,
                Background = Brushes.Transparent
            };
        }

        /// <summary>
        /// Creates a divider Border with consistent styling
        /// </summary>
        /// <returns>A styled divider Border</returns>
        public static Border CreateDivider()
        {
            return new Border
            {
                Height = 1,
                Background = (Brush)Application.Current.FindResource("AccentBorderBrush"),
                Margin = new Thickness(0, 5, 0, 5)
            };
        }

        /// <summary>
        /// Creates an Elite Dangerous styled card Border
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled Elite Dangerous card Border</returns>
        public static Border CreateEliteDangerousCard(UIElement child)
        {
            return CreateStyledBorder(child, "EliteDangerousCardStyle");
        }

        /// <summary>
        /// Creates an Elite Dangerous styled header Border
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled Elite Dangerous header Border</returns>
        public static Border CreateEliteDangerousHeader(UIElement child)
        {
            return CreateStyledBorder(child, "EliteDangerousHeaderStyle");
        }

        /// <summary>
        /// Creates an Elite Dangerous styled footer Border
        /// </summary>
        /// <param name="child">The child element to wrap</param>
        /// <returns>A styled Elite Dangerous footer Border</returns>
        public static Border CreateEliteDangerousFooter(UIElement child)
        {
            return CreateStyledBorder(child, "EliteDangerousFooterStyle");
        }

        /// <summary>
        /// Creates an Elite Dangerous styled system link Button
        /// </summary>
        /// <param name="systemName">The system name to display</param>
        /// <param name="clickHandler">The click event handler</param>
        /// <returns>A styled Elite Dangerous system link Button</returns>
        public static Button CreateEliteDangerousSystemLink(string systemName, RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                Content = systemName,
                Style = (Style)Application.Current.FindResource("EliteDangerousSystemLinkStyle")
            };
            
            if (clickHandler != null)
            {
                button.Click += clickHandler;
            }
            
            return button;
        }

        #endregion
    }
}
