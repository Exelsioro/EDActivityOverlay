using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace EDActivityOverlay.Models
{
    /// <summary>
    /// Represents a theme configuration for the application
    /// </summary>
    [XmlRoot("Theme")]
    public class Theme
    {
        [XmlAttribute("Name")]
        public string Name { get; set; } = "";

        [XmlAttribute("Description")]
        public string Description { get; set; } = "";

        [XmlAttribute("Author")]
        public string Author { get; set; } = "";

        [XmlAttribute("Version")]
        public string Version { get; set; } = "1.0";

        [XmlArray("Colors")]
        [XmlArrayItem("Color")]
        public List<ThemeColor> Colors { get; set; } = new List<ThemeColor>();

        [XmlArray("Fonts")]
        [XmlArrayItem("Font")]
        public List<ThemeFont> Fonts { get; set; } = new List<ThemeFont>();

        [XmlArray("Dimensions")]
        [XmlArrayItem("Dimension")]
        public List<ThemeDimension> Dimensions { get; set; } = new List<ThemeDimension>();

        public Theme()
        {
            Colors = new List<ThemeColor>();
            Fonts = new List<ThemeFont>();
            Dimensions = new List<ThemeDimension>();
        }
    }

    /// <summary>
    /// Represents a color definition in a theme
    /// </summary>
    public class ThemeColor
    {
        [XmlAttribute("Key")]
        public string Key { get; set; } = "";

        [XmlAttribute("Value")]
        public string Value { get; set; } = "";

        [XmlAttribute("Description")]
        public string Description { get; set; } = "";

        public ThemeColor() { }

        public ThemeColor(string key, string value, string description = "")
        {
            Key = key;
            Value = value;
            Description = description;
        }
    }

    /// <summary>
    /// Represents a font definition in a theme
    /// </summary>
    public class ThemeFont
    {
        [XmlAttribute("Key")]
        public string Key { get; set; } = "";

        [XmlAttribute("Family")]
        public string Family { get; set; } = "";

        [XmlAttribute("Size")]
        public double Size { get; set; } = 12;

        [XmlAttribute("Weight")]
        public string Weight { get; set; } = "Normal";

        [XmlAttribute("Description")]
        public string Description { get; set; } = "";

        public ThemeFont() { }

        public ThemeFont(string key, string family, double size, string weight = "Normal", string description = "")
        {
            Key = key;
            Family = family;
            Size = size;
            Weight = weight;
            Description = description;
        }
    }

    /// <summary>
    /// Represents a dimension definition in a theme
    /// </summary>
    public class ThemeDimension
    {
        [XmlAttribute("Key")]
        public string Key { get; set; } = "";

        [XmlAttribute("Value")]
        public double Value { get; set; } = 0;

        [XmlAttribute("Description")]
        public string Description { get; set; } = "";

        public ThemeDimension() { }

        public ThemeDimension(string key, double value, string description = "")
        {
            Key = key;
            Value = value;
            Description = description;
        }
    }
}
