﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Localization.Resources.AbpUi;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.Bootstrap.Microsoft.AspNetCore.Razor.TagHelpers;
using Volo.Abp.AspNetCore.Mvc.UI.Bootstrap.TagHelpers.Button;
using Volo.Abp.AspNetCore.Mvc.UI.Bootstrap.TagHelpers.Extensions;
using Volo.Abp.AspNetCore.Mvc.UI.Bootstrap.TagHelpers.Form;
using Volo.Abp.Json;

namespace Volo.Abp.AspNetCore.Mvc.UI.Bootstrap.TagHelpers.DatePicker;

public abstract class AbpDatePickerBaseTagHelperService<TTagHelper> : AbpTagHelperService<TTagHelper>
    where TTagHelper : AbpDatePickerBaseTagHelper<TTagHelper>
{
    protected readonly Dictionary<Type,Func<object,string>> SupportedInputTypes = new() {
        {typeof(string), o => DateTime.Parse((string)o).ToString("O")},
        {typeof(DateTime), o => ((DateTime) o).ToString("O")},
        {typeof(DateTime?), o => ((DateTime?) o)?.ToString("O")},
        {typeof(DateTimeOffset), o => ((DateTimeOffset) o).ToString("O")},
        {typeof(DateTimeOffset?), o => ((DateTimeOffset?) o)?.ToString("O")}
    };

    protected readonly IJsonSerializer JsonSerializer;
    protected readonly IHtmlGenerator Generator;
    protected readonly HtmlEncoder Encoder;
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IAbpTagHelperLocalizer TagHelperLocalizer;
    protected virtual string TagName { get; set; } = "abp-date-picker";
    protected IStringLocalizer<AbpUiResource> L { get; }
    protected InputTagHelper InputTagHelper { get; set; }
    protected abstract TagHelperOutput TagHelperOutput { get; set; }

    protected AbpDatePickerBaseTagHelperService(IJsonSerializer jsonSerializer, IHtmlGenerator generator,
        HtmlEncoder encoder, IServiceProvider serviceProvider, IStringLocalizer<AbpUiResource> l,
        IAbpTagHelperLocalizer tagHelperLocalizer)
    {
        JsonSerializer = jsonSerializer;
        Generator = generator;
        Encoder = encoder;
        ServiceProvider = serviceProvider;
        L = l;
        TagHelperLocalizer = tagHelperLocalizer;

        InputTagHelper = new InputTagHelper(Generator) { InputTypeName = "text" };
    }

    protected virtual T GetAttribute<T>() where T : Attribute
    {
        return GetAttributeAndModelExpression<T>(out _);
    }

    protected abstract T GetAttributeAndModelExpression<T>(out ModelExpression modelExpression) where T : Attribute;


    public async override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        TagHelperOutput = new TagHelperOutput("input", GetInputAttributes(context, output), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        InputTagHelper.ViewContext = TagHelper.ViewContext;

        if (!TagHelper.Name.IsNullOrEmpty())
        {
            InputTagHelper.Name = TagHelper.Name;
        }

        if (!TagHelper.Value.IsNullOrEmpty())
        {
            InputTagHelper.Value = TagHelper.Value;
        }

        AddDisabledAttribute(TagHelperOutput);
        AddAutoFocusAttribute(TagHelperOutput);
        AddFormControls(context, output, TagHelperOutput);
        AddPlaceholderAttribute(TagHelperOutput);
        AddInfoTextId(TagHelperOutput);

        // Open and close button
        var openButtonContent = TagHelper.OpenButton
            ? await ProcessButtonAndGetContentAsync(context, output, "calendar", "open")
            : "";
        var clearButtonContent = TagHelper.ClearButton
            ? await ProcessButtonAndGetContentAsync(context, output, "times", "clear")
            : "";

        var labelContent = TagHelper.SuppressLabel ? "" : await GetLabelAsHtmlAsync(context, output, TagHelperOutput);
        var infoContent = GetInfoAsHtml(context, output, TagHelperOutput);
        var validationContent = await GetValidationAsHtmlAsync(context, output);

        var inputGroup = new TagHelperOutput("div",
            new TagHelperAttributeList(new[] { new TagHelperAttribute("class", "input-group") }),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        inputGroup.Content.AppendHtml(
            TagHelperOutput.Render(Encoder) + openButtonContent + clearButtonContent + infoContent
        );

        var abpDatePickerTag = new TagHelperOutput(TagName, GetBaseTagAttributes(context, output),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        abpDatePickerTag.Content.AppendHtml(inputGroup.Render(Encoder));
        abpDatePickerTag.Content.AppendHtml(validationContent);
        abpDatePickerTag.Content.AppendHtml(GetExtraInputHtml(context, output));

        var innerHtml = labelContent + abpDatePickerTag.Render(Encoder);

        var order = GetOrder();

        AddGroupToFormGroupContents(
            context,
            GetPropertyName(),
            SurroundInnerHtmlAndGet(context, output, innerHtml),
            order
        );


        output.TagMode = TagMode.StartTagAndEndTag;
        output.TagName = "div";
        LeaveOnlyGroupAttributes(context, output);
        output.Attributes.AddClass("mb-3");

        output.Content.AppendHtml(innerHtml);
    }

    protected virtual TagHelperAttributeList GetInputAttributes(TagHelperContext context, TagHelperOutput output)
    {
        var groupPrefix = "group-";

        var tagHelperAttributes = output.Attributes.Where(a => !a.Name.StartsWith(groupPrefix)).ToList();

        var attrList = new TagHelperAttributeList();

        foreach (var tagHelperAttribute in tagHelperAttributes)
        {
            attrList.Add(tagHelperAttribute);
        }
        
        attrList.Add("type", "text");

        if (attrList.ContainsName("value"))
        {
            attrList.Remove(attrList.First(a => a.Name == "value"));
        }
        
        if (!TagHelper.Name.IsNullOrEmpty() && !attrList.ContainsName("name"))
        {
            attrList.Add("name", TagHelper.Name);
        }

        if (!attrList.ContainsName("autocomplete"))
        {
            attrList.Add("autocomplete", "off");
        }

        return attrList;
    }
    protected virtual void LeaveOnlyGroupAttributes(TagHelperContext context, TagHelperOutput output)
    {
        var groupPrefix = "group-";
        var tagHelperAttributes = output.Attributes.Where(a => a.Name.StartsWith(groupPrefix)).ToList();

        output.Attributes.Clear();

        foreach (var tagHelperAttribute in tagHelperAttributes)
        {
            var nameWithoutPrefix = tagHelperAttribute.Name.Substring(groupPrefix.Length);
            var newAttribute = new TagHelperAttribute(nameWithoutPrefix, tagHelperAttribute.Value);
            output.Attributes.Add(newAttribute);
        }
    }

    protected virtual string SurroundInnerHtmlAndGet(TagHelperContext context, TagHelperOutput output, string innerHtml)
    {
        return "<div class=\"mb-3\">" +
               Environment.NewLine + innerHtml + Environment.NewLine +
               "</div>";
    }

    protected abstract string GetPropertyName();

    protected virtual void AddGroupToFormGroupContents(TagHelperContext context, string propertyName, string html,
        int order)
    {
        var list = context.GetValue<List<FormGroupItem>>(FormGroupContents) ?? new List<FormGroupItem>();

        if (!list.Any(igc => igc.HtmlContent.Contains("id=\"" + propertyName.Replace('.', '_') + "\"")))
        {
            list.Add(new FormGroupItem { HtmlContent = html, Order = order, PropertyName = propertyName });
        }
    }

    protected abstract int GetOrder();
    protected abstract void AddBaseTagAttributes(TagHelperAttributeList attributes);

    protected virtual string GetExtraInputHtml(TagHelperContext context, TagHelperOutput output)
    {
        return string.Empty;
    }

    protected TagHelperAttributeList GetBaseTagAttributes(TagHelperContext context, TagHelperOutput output)
    {
        var groupPrefix = "group-";

        var tagHelperAttributes = output.Attributes.Where(a => !a.Name.StartsWith(groupPrefix)).ToList();

        var attrList = new TagHelperAttributeList();

        foreach (var tagHelperAttribute in tagHelperAttributes)
        {
            attrList.Add(tagHelperAttribute);
        }

        if (attrList.ContainsName("type"))
        {
            attrList.Remove(attrList.First(a => a.Name == "type"));
        }

        if (attrList.ContainsName("name"))
        {
            attrList.Remove(attrList.First(a => a.Name == "name"));
        }
        
        if (attrList.ContainsName("id"))
        {
            attrList.Remove(attrList.First(a => a.Name == "id"));
        }

        if (attrList.ContainsName("value"))
        {
            attrList.Remove(attrList.First(a => a.Name == "value"));
        }

        if (TagHelper.Locale != null)
        {
            attrList.Add("data-locale", JsonSerializer.Serialize(TagHelper.Locale));
        }

        if (TagHelper.MinDate != null)
        {
            attrList.Add("data-min-date", TagHelper.MinDate);
        }

        if (TagHelper.MaxDate != null)
        {
            attrList.Add("data-max-date", TagHelper.MaxDate);
        }

        if (TagHelper.MaxSpan != null)
        {
            attrList.Add("data-max-span", JsonSerializer.Serialize(TagHelper.MaxSpan));
        }

        if (TagHelper.ShowDropdowns == false)
        {
            attrList.Add("data-show-dropdowns", TagHelper.ShowDropdowns.ToString().ToLowerInvariant());
        }

        if (TagHelper.MinYear != null)
        {
            attrList.Add("data-min-year", TagHelper.MinYear);
        }

        if (TagHelper.MaxYear != null)
        {
            attrList.Add("data-max-year", TagHelper.MaxYear);
        }

        switch (TagHelper.WeekNumbers)
        {
            case AbpDatePickerWeekNumbers.Normal:
                attrList.Add("data-show-week-numbers", "true");
                break;
            case AbpDatePickerWeekNumbers.Iso:
                attrList.Add("data-show-iso-week-numbers", "true");
                break;
        }

        if (TagHelper.TimePicker != null)
        {
            attrList.Add("data-time-picker", TagHelper.TimePicker.ToString().ToLowerInvariant());
        }

        if (TagHelper.TimePickerIncrement != null)
        {
            attrList.Add("data-time-picker-increment", TagHelper.TimePickerIncrement);
        }

        if (TagHelper.TimePicker24Hour != null)
        {
            attrList.Add("data-time-picker24-hour", TagHelper.TimePicker24Hour.ToString().ToLowerInvariant());
        }

        if (TagHelper.TimePickerSeconds != null)
        {
            attrList.Add("data-time-picker-seconds", TagHelper.TimePickerSeconds.ToString().ToLowerInvariant());
        }

        if (TagHelper.Opens != AbpDatePickerOpens.Center)
        {
            attrList.Add("data-opens", TagHelper.Opens.ToString().ToLowerInvariant());
        }

        if (TagHelper.Drops != AbpDatePickerDrops.Down)
        {
            attrList.Add("data-drops", TagHelper.Drops.ToString().ToLowerInvariant());
        }

        if (!TagHelper.ButtonClasses.IsNullOrEmpty())
        {
            attrList.Add("data-button-classes", TagHelper.ButtonClasses);
        }

        if (!TagHelper.ApplyButtonClasses.IsNullOrEmpty())
        {
            attrList.Add("data-apply-button-classes", TagHelper.ApplyButtonClasses);
        }

        if (!TagHelper.CancelButtonClasses.IsNullOrEmpty())
        {
            attrList.Add("data-cancel-button-classes", TagHelper.CancelButtonClasses);
        }

        if (!TagHelper.AutoApply)
        {
            attrList.Add("data-auto-apply", TagHelper.AutoApply.ToString().ToLowerInvariant());
        }

        if (TagHelper.LinkedCalendars != null)
        {
            attrList.Add("data-linked-calendars", TagHelper.LinkedCalendars.ToString().ToLowerInvariant());
        }

        if (TagHelper.AutoUpdateInput)
        {
            attrList.Add("data-auto-update-input", TagHelper.AutoUpdateInput.ToString().ToLowerInvariant());
        }

        if (!TagHelper.ParentEl.IsNullOrEmpty())
        {
            attrList.Add("data-parent-el", TagHelper.ParentEl);
        }

        if (!TagHelper.DateFormat.IsNullOrEmpty())
        {
            attrList.Add("data-date-format", TagHelper.DateFormat);
        }
        
        if(TagHelper.Ranges != null && TagHelper.Ranges.Any())
        {
            var ranges = TagHelper.Ranges.ToDictionary(r => r.Label, r => r.Dates);
            
            attrList.Add("data-ranges", JsonSerializer.Serialize(ranges));
        }
        
        if(TagHelper.AlwaysShowCalendars)
        {
            attrList.Add("data-always-show-calendars", TagHelper.AlwaysShowCalendars.ToString().ToLowerInvariant());
        }
        
        if(TagHelper.ShowCustomRangeLabel == false)
        {
            attrList.Add("data-show-custom-range-label", TagHelper.ShowCustomRangeLabel.ToString().ToLowerInvariant());
        }
        
        if(TagHelper.Options != null)
        {
            attrList.Add("data-options", JsonSerializer.Serialize(TagHelper.Options));
        }
        
        if (TagHelper.IsUtc)
        {
            attrList.Add("data-is-utc", "true");
        }
        
        if (TagHelper.IsIso)
        {
            attrList.Add("data-is-iso", TagHelper.IsIso.ToString().ToLowerInvariant());
        }

        AddBaseTagAttributes(attrList);

        return attrList;
    }

    protected virtual bool IsOutputHidden(TagHelperOutput inputTag)
    {
        return inputTag.Attributes.Any(a =>
            a.Name.ToLowerInvariant() == "type" && a.Value?.ToString()?.ToLowerInvariant() == "hidden");
    }

    protected virtual string GetInfoAsHtml(TagHelperContext context, TagHelperOutput output, TagHelperOutput inputTag)
    {
        if (IsOutputHidden(inputTag))
        {
            return string.Empty;
        }

        string text;
        ModelExplorer modelExplorer = null;

        if (!string.IsNullOrEmpty(TagHelper.InfoText))
        {
            text = TagHelper.InfoText;
        }
        else
        {
            var infoAttribute = GetAttributeAndModelExpression<InputInfoText>(out var modelExpression);
            if (infoAttribute != null)
            {
                modelExplorer = modelExpression.ModelExplorer;
                text = infoAttribute.Text;
            }
            else
            {
                return string.Empty;
            }
        }

        var idAttr = inputTag.Attributes.FirstOrDefault(a => a.Name == "id");
        var localizedText = TagHelperLocalizer.GetLocalizedText(text, modelExplorer);

        var div = new TagBuilder("div");
        div.Attributes.Add("id", idAttr?.Value + "InfoText");
        div.AddCssClass("form-text");
        div.InnerHtml.Append(localizedText);

        inputTag.Attributes.Add("aria-describedby", idAttr?.Value + "InfoText");

        return div.ToHtmlString();
    }

    protected virtual async Task<string> GetLabelAsHtmlAsync(TagHelperContext context, TagHelperOutput output,
        TagHelperOutput inputTag)
    {
        if (string.IsNullOrEmpty(TagHelper.Label))
        {
            return await GetLabelAsHtmlUsingTagHelperAsync(context, output) + GetRequiredSymbol(context, output);
        }

        var label = new TagBuilder("label");
        label.Attributes.Add("for", GetIdAttributeValue(inputTag));
        label.InnerHtml.AppendHtml(TagHelper.Label);

        label.AddCssClass("form-label");

        if (!TagHelper.LabelTooltip.IsNullOrEmpty())
        {
            label.Attributes.Add("data-bs-toggle", "tooltip");
            label.Attributes.Add("data-bs-placement", TagHelper.LabelTooltipPlacement);
            if (TagHelper.LabelTooltipHtml)
            {
                label.Attributes.Add("data-bs-html", "true");
            }

            label.Attributes.Add("title", TagHelper.LabelTooltip);
            label.InnerHtml.AppendHtml($" <i class=\"bi {TagHelper.LabelTooltipIcon}\"></i>");
        }

        return label.ToHtmlString();
    }

    protected virtual string GetIdAttributeValue(TagHelperOutput inputTag)
    {
        var idAttr = inputTag.Attributes.FirstOrDefault(a => a.Name == "id");

        return idAttr != null ? idAttr.Value.ToString() : string.Empty;
    }

    protected virtual string GetRequiredSymbol(TagHelperContext context, TagHelperOutput output)
    {
        if (!TagHelper.DisplayRequiredSymbol)
        {
            return "";
        }

        return GetAttribute<RequiredAttribute>() != null ? "<span> * </span>" : "";
    }

    protected abstract ModelExpression GetModelExpression();
    
    protected virtual async Task<string> GetLabelAsHtmlUsingTagHelperAsync(TagHelperContext context,
        TagHelperOutput output)
    {
        var labelTagHelper = new LabelTagHelper(Generator) {
            ViewContext = TagHelper.ViewContext,
            For = GetModelExpression()
        };

        var attributeList = new TagHelperAttributeList();

        attributeList.AddClass("form-label");

        if (!TagHelper.LabelTooltip.IsNullOrEmpty())
        {
            attributeList.Add("data-bs-toggle", "tooltip");
            attributeList.Add("data-bs-placement", TagHelper.LabelTooltipPlacement);
            if (TagHelper.LabelTooltipHtml)
            {
                attributeList.Add("data-bs-html", "true");
            }

            attributeList.Add("title", TagHelper.LabelTooltip);
        }

        var innerOutput =
            await labelTagHelper.ProcessAndGetOutputAsync(attributeList, context, "label", TagMode.StartTagAndEndTag);
        if (!TagHelper.LabelTooltip.IsNullOrEmpty())
        {
            innerOutput.Content.AppendHtml($" <i class=\"bi {TagHelper.LabelTooltipIcon}\"></i>");
        }

        return innerOutput.Render(Encoder);
    }

    protected virtual async Task<string> ProcessButtonAndGetContentAsync(TagHelperContext context,
        TagHelperOutput output, string icon, string type)
    {
        var abpButtonTagHelper = ServiceProvider.GetRequiredService<AbpButtonTagHelper>();
        var attributes =
            new TagHelperAttributeList { new("type", "button"), new("tabindex", "-1"), new("data-type", type) };
        abpButtonTagHelper.ButtonType = AbpButtonType.Outline_Secondary;
        abpButtonTagHelper.Icon = icon;

        return await abpButtonTagHelper.RenderAsync(attributes, context, Encoder, "button", TagMode.StartTagAndEndTag);
    }

    protected virtual void AddInfoTextId(TagHelperOutput inputTagHelperOutput)
    {
        if (GetAttribute<InputInfoText>() == null)
        {
            return;
        }

        var idAttr = inputTagHelperOutput.Attributes.FirstOrDefault(a => a.Name == "id");

        if (idAttr == null)
        {
            return;
        }

        inputTagHelperOutput.Attributes.Add("aria-describedby", GetInfoText());
    }
    
    public virtual string GetInfoText()
    {
        var infoAttribute = GetAttributeAndModelExpression<InputInfoText>(out var modelExpression);

        if (infoAttribute != null)
        {
            return TagHelperLocalizer.GetLocalizedText(infoAttribute.Text, modelExpression.ModelExplorer);
        }

        return string.Empty;
    }

    protected virtual void AddPlaceholderAttribute(TagHelperOutput inputTagHelperOutput)
    {
        if (inputTagHelperOutput.Attributes.ContainsName("placeholder"))
        {
            return;
        }

        var attribute = GetAttributeAndModelExpression<Placeholder>(out var modelExpression);

        if (attribute != null)
        {
            var placeholderLocalized =
                TagHelperLocalizer.GetLocalizedText(attribute.Value, modelExpression.ModelExplorer);

            inputTagHelperOutput.Attributes.Add("placeholder", placeholderLocalized);
        }
    }

    protected virtual void AddFormControls(TagHelperContext context, TagHelperOutput output,
        TagHelperOutput inputTagHelperOutput)
    {
        inputTagHelperOutput.Attributes.AddClass("form-control");
        var size = GetSize(context, output);
        if (!size.IsNullOrEmpty())
        {
            inputTagHelperOutput.Attributes.AddClass(size);
        }
    }

    protected virtual void AddAutoFocusAttribute(TagHelperOutput inputTagHelperOutput)
    {
        if (TagHelper.AutoFocus && !inputTagHelperOutput.Attributes.ContainsName("data-auto-focus"))
        {
            inputTagHelperOutput.Attributes.Add("data-auto-focus", "true");
        }
    }

    protected virtual void AddDisabledAttribute(TagHelperOutput inputTagHelperOutput)
    {
        if (inputTagHelperOutput.Attributes.ContainsName("disabled") == false &&
            (TagHelper.IsDisabled || GetAttribute<DisabledInput>() != null))
        {
            inputTagHelperOutput.Attributes.Add("disabled", "");
        }
    }


    protected virtual string GetSize(TagHelperContext context, TagHelperOutput output)
    {
        // TODO: Test this method
        var attribute = GetAttribute<FormControlSize>();

        if (attribute != null)
        {
            TagHelper.Size = attribute.Size;
        }

        return TagHelper.Size switch {
            AbpFormControlSize.Small => "form-control-sm",
            AbpFormControlSize.Medium => "form-control-md",
            AbpFormControlSize.Large => "form-control-lg",
            _ => ""
        };
    }

    protected abstract Task<string> GetValidationAsHtmlAsync(TagHelperContext context, TagHelperOutput output);

    protected virtual async Task<string> GetValidationAsHtmlByInputAsync(TagHelperContext context,
        TagHelperOutput output,
        [NotNull]InputTagHelper inputTag)
    {
        var validationMessageTagHelper =
            new ValidationMessageTagHelper(Generator) { For = inputTag.For, ViewContext = TagHelper.ViewContext };

        var attributeList = new TagHelperAttributeList { { "class", "text-danger col-auto" } };

        return await validationMessageTagHelper.RenderAsync(attributeList, context, Encoder, "span",
            TagMode.StartTagAndEndTag);
    }
}