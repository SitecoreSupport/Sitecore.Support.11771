namespace Sitecore.Support.XA.Foundation.PlaceholderSettings.Pipelines.GetXmlBasedLayoutDefinition
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Xml.Linq;
  using Microsoft.Extensions.DependencyInjection;
  using Sitecore.Data.Fields;
  using Sitecore.Data.Items;
  using Sitecore.Mvc.Pipelines.Response.GetXmlBasedLayoutDefinition;
  using Sitecore.XA.Foundation.Presentation;
  using Sitecore.XA.Foundation.Presentation.Layout;
  using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;

  public class AddPartialDesignsPlaceholderSettings : GetFromLayoutField
  {
    private readonly string Prefix = "sxa";

    public override void Process(GetXmlBasedLayoutDefinitionArgs args)
    {
      Item contextItem = args.ContextItem;
      if (contextItem == null)
      {
        return;
      }

      Item designItem = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService<IPresentationContext>().GetDesignItem(contextItem);
      if (designItem == null && contextItem.TemplateID != Templates.PartialDesign.ID)
      {
        return;
      }

      List<XElement> placeholderSettings = new List<XElement>();

      if (designItem != null)
      {
        placeholderSettings.AddRange(GetDesignPlaceholderSettings(designItem));
      }

      if (contextItem.InheritsFrom(Templates.PartialDesign.ID))
      {
        placeholderSettings.AddRange(GetBasePartialDesignPlaceholderSettings(contextItem));
      }
      if (placeholderSettings.Any())
      {
        MergePartialDesignsPlaceholderSettings(args, placeholderSettings);
      }
    }

    public virtual IEnumerable<XElement> GetDesignPlaceholderSettings(Item item)
    {
      MultilistField injectedItems = item.Fields[Templates.Design.Fields.PartialDesigns];

      if (injectedItems == null)
      {
        return null;
      }

      return injectedItems.GetItems().Reverse().Select(s =>
      {
        List<XElement> partialDesignRendrings = GetBasePartialDesignPlaceholderSettings(s).ToList();
        XElement phSettingsContainer = GetFromField(s);
        if (phSettingsContainer != null)
        {
          partialDesignRendrings.AddRange(phSettingsContainer.Elements("d").ToList());
        }
        return partialDesignRendrings;
      }).SelectMany(x => x);
    }

    protected override XElement GetFromField(Item item)
    {
      LayoutField field = new LayoutField(item);

      if (string.IsNullOrWhiteSpace(field.Value))
      {
        return null;
      }

      XElement partialDesignPlaceholderSettings = XDocument.Parse(field.Value).Root;
      if (partialDesignPlaceholderSettings != null)
      {
        var additionalPhSettings = new List<XElement>();
        Dictionary<string, HashSet<string>> deviceWrappersSignatures = new Dictionary<string, HashSet<string>>();
        foreach (XElement devicePhSettings in partialDesignPlaceholderSettings.Descendants("d"))
        {
          foreach (XElement phSetting in devicePhSettings.Descendants("p"))
          {
            //add partial design id attribute
            phSetting.Add(new XAttribute("sid", item.ID));
            var partialDesignSignature = item[Templates.PartialDesign.Fields.Signature];
            if (partialDesignSignature != null)
            {
              additionalPhSettings.Add(CreateInjectedPhSetting(item, phSetting, partialDesignSignature));
            }
          }
          additionalPhSettings.ForEach(devicePhSettings.Add);
        }

      }
      return partialDesignPlaceholderSettings;
    }

    protected virtual XElement CreateInjectedPhSetting(Item item, XElement srcPhSetting, string partialDesignSignature)
    {
      var phSetting = new XElement(srcPhSetting);
      phSetting.SetAttributeValue("uid", GetNewUid());
      var phKey = phSetting.Attribute("key")?.Value;
      if (phKey != null && phKey.Contains("/"))
      {
        var placeholderParts = new Placeholder(phKey).Parts;
        string currentPhPrefix = placeholderParts.FirstOrDefault(p => p.Contains($"{Prefix}-"));
        bool isInheritedRendering = !string.IsNullOrWhiteSpace(currentPhPrefix) && GetAllBasePartialDesignSignatures(item).Contains(currentPhPrefix);

        if (!isInheritedRendering)
        {
          string wrapperPhPrefix = $"/{placeholderParts[0]}/{Prefix}-{partialDesignSignature}";
          var newValue = phKey.Replace($"/{placeholderParts[0]}", wrapperPhPrefix);
          phSetting.SetAttributeValue("key", newValue);
        }
      }
      else
      {
        var newValue = $"/{phKey}/{Prefix}-{partialDesignSignature}";
        phSetting.SetAttributeValue("key", newValue);
      }
      return phSetting;
    }

    protected virtual string GetNewUid()
    {
      return $"{{{Guid.NewGuid().ToString().ToUpper()}}}";
    }

    protected virtual void MergePartialDesignsPlaceholderSettings(GetXmlBasedLayoutDefinitionArgs args, IEnumerable<XElement> designPhSettings)
    {
      //list of already defined devices 
      IEnumerable<XElement> deviceElements = args.Result.Elements(XName.Get("d"));

      //all of the placeholder settings defined in the already defined devices
      Dictionary<string, List<XElement>> phSettings = deviceElements.ToDictionary(d => $"{d.Attribute("id").Value}#{d.Attribute("l").Value}", deviceElement => deviceElement.Elements("p").ToList());

      //get placeholder settings from partial designs and put them at the begining of each device
      foreach (XElement phSetting in designPhSettings)
      {
        string key = $"{phSetting.Attribute("id").Value}#{phSetting.Attribute("l").Value}";
        if (phSettings.ContainsKey(key))
        {
          phSettings[key].InsertRange(0, phSetting.Elements("p"));
        }
        else
        {
          phSettings.Add(key, phSetting.Elements("p").ToList());
        }
      }

      //clear current placeholder settings and add existing ones merged with those from partial design
      if (phSettings.Any(x => x.Value.Count > 0))
      {
        foreach (string key in phSettings.Keys)
        {
          if (phSettings[key].Any())
          {
            string[] deviceData = key.Split('#');

            args.Result.Descendants("d").Where(d => d.Attribute("id").Value == deviceData[0] && d.Attribute("l").Value == deviceData[1]).Remove();

            XElement deviceElement = new XElement("d", new XAttribute("id", deviceData[0]), new XAttribute("l", deviceData[1]));
            foreach (XElement r in phSettings[key])
            {
              deviceElement.Add(r);
            }
            args.Result.Add(deviceElement);
          }
        }
      }
    }

    protected virtual IEnumerable<XElement> GetBasePartialDesignPlaceholderSettings(Item partialDesign)
    {
      List<XElement> phSettings = new List<XElement>();
      MultilistField basePartialDesignsField = partialDesign.Fields[Templates.PartialDesign.Fields.BasePartialDesign];
      if (basePartialDesignsField != null)
      {
        foreach (Item basePartialDesign in basePartialDesignsField.GetItems().Where(s => s.ID != partialDesign.ID))
        {
          XElement phSettingsContainer = GetFromField(basePartialDesign);
          if (phSettingsContainer != null)
          {
            phSettings.AddRange(phSettingsContainer.Elements("d"));
          }
          phSettings.AddRange(GetBasePartialDesignPlaceholderSettings(basePartialDesign));
        }
      }
      return phSettings;
    }

    protected virtual List<string> GetAllBasePartialDesignSignatures(Item partialDesign)
    {
      List<string> signatures = new List<string>();
      MultilistField basePartialDesignField = partialDesign.Fields[Templates.PartialDesign.Fields.BasePartialDesign];
      if (basePartialDesignField != null)
      {
        foreach (Item basePartialDesign in basePartialDesignField.GetItems().Where(s => s.ID != partialDesign.ID))
        {
          signatures.Add($"{Prefix}-{basePartialDesign[Templates.PartialDesign.Fields.Signature]}");
          signatures.AddRange(GetAllBasePartialDesignSignatures(basePartialDesign));
        }
      }
      return signatures;
    }
  }
}