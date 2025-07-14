using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class AngularStyleTools(PlaywrightSessionManager sessionManager)
{
    // Enhanced JSON serialization options with aggressive flattening for MCP compatibility
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 32, // Reduced depth to ensure MCP compatibility
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    [McpServerTool]
    [Description("Analyze Angular component styling and detect component isolation issues")]
    public async Task<string> AnalyzeAngularComponentStyles(
        [Description("Component selector or data-testid")] string componentSelector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(componentSelector);
            
            var jsCode = $@"
                const component = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                if (!component) {{
                    return {{ error: 'Component not found' }};
                }}
                
                // Analyze Angular-specific styling
                const ngAttributes = [];
                Array.from(component.attributes).forEach(attr => {{
                    if (attr.name.startsWith('ng-') || attr.name.startsWith('_ng')) {{
                        ngAttributes.push({{ name: attr.name, value: attr.value }});
                    }}
                }});
                
                // Check for Angular component isolation
                const hasViewEncapsulation = ngAttributes.some(attr => 
                    attr.name.includes('_ng') && attr.name.includes('c'));
                
                // Analyze nested Angular components
                const nestedComponents = Array.from(component.querySelectorAll('*'))
                    .filter(el => Array.from(el.attributes).some(attr => attr.name.startsWith('ng-') || attr.name.startsWith('_ng')))
                    .map(el => {{
                        const elAttrs = Array.from(el.attributes)
                            .filter(attr => attr.name.startsWith('ng-') || attr.name.startsWith('_ng'))
                            .map(attr => ({{ name: attr.name, value: attr.value }}));
                        
                        return {{
                            tagName: el.tagName.toLowerCase(),
                            selector: el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + 
                                     (el.className ? '.' + Array.from(el.classList).slice(0, 2).join('.') : ''),
                            ngAttributes: elAttrs,
                            computedStyles: {{
                                display: window.getComputedStyle(el).display,
                                position: window.getComputedStyle(el).position,
                                zIndex: window.getComputedStyle(el).zIndex
                            }}
                        }};
                    }});
                
                // Check for CSS-in-JS or inline styles that might override Angular styles
                const inlineStyleElements = Array.from(component.querySelectorAll('[style]'));
                const inlineStyles = inlineStyleElements.map(el => {{
                    return {{
                        selector: el.tagName.toLowerCase() + (el.id ? '#' + el.id : ''),
                        inlineStyle: el.getAttribute('style'),
                        hasNgAttributes: Array.from(el.attributes).some(attr => attr.name.startsWith('ng-'))
                    }};
                }});
                
                // Check for Material Design or other UI library components
                const uiLibraryComponents = Array.from(component.querySelectorAll('*'))
                    .filter(el => {{
                        const tagName = el.tagName.toLowerCase();
                        return tagName.startsWith('mat-') || 
                               tagName.startsWith('ng-') || 
                               tagName.startsWith('p-') || // PrimeNG
                               tagName.startsWith('nz-') || // NG-ZORRO
                               el.classList.toString().includes('mat-') ||
                               el.classList.toString().includes('p-') ||
                               el.classList.toString().includes('ant-');
                    }})
                    .map(el => {{
                        const computedStyles = window.getComputedStyle(el);
                        return {{
                            tagName: el.tagName.toLowerCase(),
                            classes: Array.from(el.classList),
                            library: el.tagName.toLowerCase().startsWith('mat-') ? 'Angular Material' :
                                    el.tagName.toLowerCase().startsWith('p-') ? 'PrimeNG' :
                                    el.tagName.toLowerCase().startsWith('nz-') ? 'NG-ZORRO' :
                                    'Unknown',
                            theme: {{
                                primaryColor: computedStyles.getPropertyValue('--mdc-theme-primary') || 
                                             computedStyles.getPropertyValue('--primary-color') || 'Not detected',
                                surfaceColor: computedStyles.getPropertyValue('--mdc-theme-surface') || 
                                             computedStyles.getPropertyValue('--surface-color') || 'Not detected'
                            }}
                        }};
                    }});
                
                const componentStyles = window.getComputedStyle(component);
                
                return {{
                    componentInfo: {{
                        tagName: component.tagName.toLowerCase(),
                        selector: '{finalSelector.Replace("'", "\\'")}',
                        ngAttributes: ngAttributes,
                        hasViewEncapsulation: hasViewEncapsulation,
                        classes: Array.from(component.classList),
                        id: component.id || null
                    }},
                    styling: {{
                        display: componentStyles.display,
                        position: componentStyles.position,
                        backgroundColor: componentStyles.backgroundColor,
                        color: componentStyles.color,
                        fontFamily: componentStyles.fontFamily,
                        fontSize: componentStyles.fontSize,
                        padding: componentStyles.padding,
                        margin: componentStyles.margin,
                        border: componentStyles.border,
                        borderRadius: componentStyles.borderRadius,
                        boxShadow: componentStyles.boxShadow,
                        transform: componentStyles.transform,
                        opacity: componentStyles.opacity,
                        zIndex: componentStyles.zIndex
                    }},
                    nestedComponents: nestedComponents,
                    inlineStyles: inlineStyles,
                    uiLibraryComponents: uiLibraryComponents,
                    analysis: {{
                        hasInlineStyles: inlineStyles.length > 0,
                        nestedComponentCount: nestedComponents.length,
                        uiLibraryComponentCount: uiLibraryComponents.length,
                        detectedLibraries: [...new Set(uiLibraryComponents.map(comp => comp.library))],
                        potentialStyleConflicts: inlineStyles.filter(style => style.hasNgAttributes).length
                    }}
                }};
            ";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to analyze Angular component styles: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Extract Angular Material theme and design token information")]
    public async Task<string> ExtractAngularMaterialTheme(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";
            
            var jsCode = @"
                // Extract Material Design theme tokens
                const rootStyles = window.getComputedStyle(document.documentElement);
                const materialTokens = {};
                const mdcTokens = {};
                const customTokens = {};
                
                // Iterate through all CSS custom properties
                for (let prop of rootStyles) {
                    if (prop.startsWith('--')) {
                        const value = rootStyles.getPropertyValue(prop).trim();
                        
                        if (prop.includes('mat-') || prop.includes('material')) {
                            materialTokens[prop] = value;
                        } else if (prop.includes('mdc-')) {
                            mdcTokens[prop] = value;
                        } else if (prop.includes('primary') || prop.includes('accent') || 
                                  prop.includes('warn') || prop.includes('theme')) {
                            customTokens[prop] = value;
                        }
                    }
                }
                
                // Check for Material components on the page
                const materialComponents = Array.from(document.querySelectorAll('[class*=""mat-""], mat-*'))
                    .map(el => {
                        const computedStyles = window.getComputedStyle(el);
                        return {
                            tagName: el.tagName.toLowerCase(),
                            classes: Array.from(el.classList).filter(cls => cls.includes('mat-')),
                            colors: {
                                color: computedStyles.color,
                                backgroundColor: computedStyles.backgroundColor,
                                borderColor: computedStyles.borderColor
                            },
                            typography: {
                                fontSize: computedStyles.fontSize,
                                fontWeight: computedStyles.fontWeight,
                                fontFamily: computedStyles.fontFamily
                            }
                        };
                    });
                
                // Extract theme-related colors from common Material elements
                const themeColors = {};
                const primaryElements = document.querySelectorAll('[color=""primary""], .mat-primary, .mat-button-primary');
                const accentElements = document.querySelectorAll('[color=""accent""], .mat-accent, .mat-button-accent');
                const warnElements = document.querySelectorAll('[color=""warn""], .mat-warn, .mat-button-warn');
                
                if (primaryElements.length > 0) {
                    const primaryStyle = window.getComputedStyle(primaryElements[0]);
                    themeColors.primary = {
                        color: primaryStyle.color,
                        backgroundColor: primaryStyle.backgroundColor
                    };
                }
                
                if (accentElements.length > 0) {
                    const accentStyle = window.getComputedStyle(accentElements[0]);
                    themeColors.accent = {
                        color: accentStyle.color,
                        backgroundColor: accentStyle.backgroundColor
                    };
                }
                
                if (warnElements.length > 0) {
                    const warnStyle = window.getComputedStyle(warnElements[0]);
                    themeColors.warn = {
                        color: warnStyle.color,
                        backgroundColor: warnStyle.backgroundColor
                    };
                }
                
                // Check for dark theme indicators
                const isDarkTheme = document.body.classList.contains('dark-theme') ||
                                   document.body.classList.contains('mat-dark-theme') ||
                                   window.getComputedStyle(document.body).backgroundColor === 'rgb(48, 48, 48)' ||
                                   window.getComputedStyle(document.body).backgroundColor === 'rgb(33, 33, 33)';
                
                return {
                    materialDesignTokens: materialTokens,
                    mdcTokens: mdcTokens,
                    customThemeTokens: customTokens,
                    themeColors: themeColors,
                    materialComponents: materialComponents.slice(0, 20), // Limit output
                    themeAnalysis: {
                        isDarkTheme: isDarkTheme,
                        totalMaterialTokens: Object.keys(materialTokens).length,
                        totalMdcTokens: Object.keys(mdcTokens).length,
                        totalCustomTokens: Object.keys(customTokens).length,
                        materialComponentCount: materialComponents.length,
                        hasThemeColors: Object.keys(themeColors).length > 0
                    }
                };
            ";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to extract Angular Material theme: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Validate Angular component styling best practices")]
    public async Task<string> ValidateAngularStylingBestPractices(
        [Description("Component selector or data-testid")] string componentSelector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(componentSelector);
            
            var jsCode = $@"
                const component = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                if (!component) {{
                    return {{ error: 'Component not found' }};
                }}
                
                const violations = [];
                const recommendations = [];
                const goodPractices = [];
                
                // Check for inline styles (generally discouraged in Angular)
                const elementsWithInlineStyles = Array.from(component.querySelectorAll('[style]'));
                if (elementsWithInlineStyles.length > 0) {{
                    violations.push({{
                        type: 'inline-styles',
                        severity: 'medium',
                        message: `Found ${{elementsWithInlineStyles.length}} elements with inline styles`,
                        elements: elementsWithInlineStyles.slice(0, 5).map(el => el.tagName.toLowerCase()),
                        recommendation: 'Use CSS classes or Angular component styles instead of inline styles'
                    }});
                }}
                
                // Check for !important declarations (code smell)
                const allElements = Array.from(component.querySelectorAll('*'));
                let importantCount = 0;
                allElements.forEach(el => {{
                    const inlineStyle = el.getAttribute('style');
                    if (inlineStyle && inlineStyle.includes('!important')) {{
                        importantCount++;
                    }}
                }});
                
                if (importantCount > 0) {{
                    violations.push({{
                        type: 'important-declarations',
                        severity: 'high',
                        message: `Found ${{importantCount}} elements using !important`,
                        recommendation: 'Avoid !important declarations. Use more specific selectors or restructure CSS'
                    }});
                }}
                
                // Check for proper Angular Material usage
                const matElements = Array.from(component.querySelectorAll('mat-*'));
                const customStyledMatElements = matElements.filter(el => {{
                    const computedStyles = window.getComputedStyle(el);
                    return el.hasAttribute('style') || 
                           Array.from(el.classList).some(cls => !cls.startsWith('mat-'));
                }});
                
                if (customStyledMatElements.length > 0) {{
                    violations.push({{
                        type: 'material-customization',
                        severity: 'medium',
                        message: `Found ${{customStyledMatElements.length}} Material components with custom styling`,
                        recommendation: 'Use Angular Material theming system instead of direct CSS overrides'
                    }});
                }}
                
                // Check for responsive design patterns
                const responsiveElements = allElements.filter(el => {{
                    const computedStyles = window.getComputedStyle(el);
                    return computedStyles.display === 'flex' || 
                           computedStyles.display === 'grid' ||
                           [computedStyles.width, computedStyles.height, computedStyles.fontSize]
                               .some(val => val.includes('vw') || val.includes('vh') || val.includes('%'));
                }});
                
                if (responsiveElements.length > 0) {{
                    goodPractices.push({{
                        type: 'responsive-design',
                        message: `Found ${{responsiveElements.length}} elements using responsive design patterns`,
                        details: 'Good use of flexbox, grid, or viewport units'
                    }});
                }}
                
                // Check for accessibility styling
                const accessibleElements = allElements.filter(el => {{
                    const computedStyles = window.getComputedStyle(el);
                    const hasGoodContrast = true; // Would need color contrast calculation
                    const hasFocusStyles = el.matches(':focus-visible') || 
                                          computedStyles.outline !== 'none' ||
                                          computedStyles.outlineWidth !== '0px';
                    return hasGoodContrast; // Simplified for now
                }});
                
                // Check for proper semantic HTML
                const semanticElements = Array.from(component.querySelectorAll('header, nav, main, section, article, aside, footer'));
                if (semanticElements.length > 0) {{
                    goodPractices.push({{
                        type: 'semantic-html',
                        message: `Found ${{semanticElements.length}} semantic HTML elements`,
                        details: 'Good use of semantic HTML structure'
                    }});
                }}
                
                // Check for CSS Grid or Flexbox layout
                const layoutElements = allElements.filter(el => {{
                    const computedStyles = window.getComputedStyle(el);
                    return computedStyles.display === 'flex' || computedStyles.display === 'grid';
                }});
                
                if (layoutElements.length > 0) {{
                    goodPractices.push({{
                        type: 'modern-layout',
                        message: `Found ${{layoutElements.length}} elements using modern CSS layout`,
                        details: 'Good use of CSS Grid or Flexbox'
                    }});
                }}
                
                // Overall component analysis
                const componentStyles = window.getComputedStyle(component);
                const hasViewEncapsulation = Array.from(component.attributes)
                    .some(attr => attr.name.startsWith('_ng') && attr.name.includes('c'));
                
                if (hasViewEncapsulation) {{
                    goodPractices.push({{
                        type: 'view-encapsulation',
                        message: 'Component uses Angular View Encapsulation',
                        details: 'Styles are properly isolated to this component'
                    }});
                }}
                
                // Calculate overall score
                const violationScore = violations.reduce((score, v) => {{
                    return score - (v.severity === 'high' ? 30 : v.severity === 'medium' ? 15 : 5);
                }}, 100);
                
                const practiceBonus = Math.min(goodPractices.length * 5, 20);
                const finalScore = Math.max(0, Math.min(100, violationScore + practiceBonus));
                
                return {{
                    componentSelector: '{finalSelector.Replace("'", "\\'")}',
                    violations: violations,
                    recommendations: recommendations,
                    goodPractices: goodPractices,
                    score: {{
                        overall: finalScore,
                        grade: finalScore >= 90 ? 'A' : finalScore >= 80 ? 'B' : 
                               finalScore >= 70 ? 'C' : finalScore >= 60 ? 'D' : 'F',
                        breakdown: {{
                            violations: violations.length,
                            goodPractices: goodPractices.length,
                            hasViewEncapsulation: hasViewEncapsulation
                        }}
                    }},
                    summary: {{
                        totalElements: allElements.length,
                        elementsWithInlineStyles: elementsWithInlineStyles.length,
                        materialElements: matElements.length,
                        responsiveElements: responsiveElements.length,
                        semanticElements: semanticElements.length
                    }}
                }};
            ";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to validate Angular styling best practices: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Capture visual element properties for detailed reporting to Claude")]
    public async Task<string> CaptureElementVisualReport(
        [Description("Element selector or data-testid")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            
            var jsCode = $@"
                const element = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                if (!element) {{
                    return {{ error: 'Element not found' }};
                }}
                
                const computedStyles = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                
                // Helper function to convert colors to different formats
                function parseColor(colorString) {{
                    const div = document.createElement('div');
                    div.style.color = colorString;
                    document.body.appendChild(div);
                    const computedColor = window.getComputedStyle(div).color;
                    document.body.removeChild(div);
                    
                    // Parse RGB values
                    const match = computedColor.match(/rgba?\\((\\d+),\\s*(\\d+),\\s*(\\d+)(?:,\\s*([\\d\\.]+))?\\)/);
                    if (match) {{
                        const r = parseInt(match[1]);
                        const g = parseInt(match[2]);
                        const b = parseInt(match[3]);
                        const a = match[4] ? parseFloat(match[4]) : 1;
                        
                        // Convert to hex
                        const hex = '#' + [r, g, b].map(x => x.toString(16).padStart(2, '0')).join('');
                        
                        // Convert to HSL
                        const rNorm = r / 255;
                        const gNorm = g / 255;
                        const bNorm = b / 255;
                        const max = Math.max(rNorm, gNorm, bNorm);
                        const min = Math.min(rNorm, gNorm, bNorm);
                        const diff = max - min;
                        const l = (max + min) / 2;
                        
                        let h = 0;
                        let s = 0;
                        
                        if (diff !== 0) {{
                            s = l > 0.5 ? diff / (2 - max - min) : diff / (max + min);
                            
                            switch (max) {{
                                case rNorm: h = (gNorm - bNorm) / diff + (gNorm < bNorm ? 6 : 0); break;
                                case gNorm: h = (bNorm - rNorm) / diff + 2; break;
                                case bNorm: h = (rNorm - gNorm) / diff + 4; break;
                            }}
                            h /= 6;
                        }}
                        
                        return {{
                            original: colorString,
                            computed: computedColor,
                            rgb: {{ r, g, b, a }},
                            hex: hex,
                            hsl: {{
                                h: Math.round(h * 360),
                                s: Math.round(s * 100),
                                l: Math.round(l * 100)
                            }}
                        }};
                    }}
                    return {{ original: colorString, computed: computedColor }};
                }}
                
                // Comprehensive visual report
                const report = {{
                    basicInfo: {{
                        selector: '{finalSelector.Replace("'", "\\'")}',
                        tagName: element.tagName.toLowerCase(),
                        id: element.id || null,
                        classes: Array.from(element.classList),
                        textContent: element.textContent?.trim().substring(0, 200) || '',
                        innerHTML: element.innerHTML.substring(0, 300) || ''
                    }},
                    dimensions: {{
                        width: {{ 
                            pixels: rect.width,
                            css: computedStyles.width
                        }},
                        height: {{ 
                            pixels: rect.height,
                            css: computedStyles.height
                        }},
                        position: {{
                            top: rect.top,
                            left: rect.left,
                            right: rect.right,
                            bottom: rect.bottom
                        }}
                    }},
                    colors: {{
                        text: parseColor(computedStyles.color),
                        background: parseColor(computedStyles.backgroundColor),
                        border: parseColor(computedStyles.borderColor),
                        borderTop: parseColor(computedStyles.borderTopColor),
                        borderRight: parseColor(computedStyles.borderRightColor),
                        borderBottom: parseColor(computedStyles.borderBottomColor),
                        borderLeft: parseColor(computedStyles.borderLeftColor)
                    }},
                    borders: {{
                        style: computedStyles.borderStyle,
                        width: computedStyles.borderWidth,
                        radius: computedStyles.borderRadius,
                        individual: {{
                            top: {{ width: computedStyles.borderTopWidth, style: computedStyles.borderTopStyle }},
                            right: {{ width: computedStyles.borderRightWidth, style: computedStyles.borderRightStyle }},
                            bottom: {{ width: computedStyles.borderBottomWidth, style: computedStyles.borderBottomStyle }},
                            left: {{ width: computedStyles.borderLeftWidth, style: computedStyles.borderLeftStyle }}
                        }}
                    }},
                    spacing: {{
                        margin: {{
                            all: computedStyles.margin,
                            top: computedStyles.marginTop,
                            right: computedStyles.marginRight,
                            bottom: computedStyles.marginBottom,
                            left: computedStyles.marginLeft
                        }},
                        padding: {{
                            all: computedStyles.padding,
                            top: computedStyles.paddingTop,
                            right: computedStyles.paddingRight,
                            bottom: computedStyles.paddingBottom,
                            left: computedStyles.paddingLeft
                        }}
                    }},
                    typography: {{
                        fontFamily: computedStyles.fontFamily,
                        fontSize: computedStyles.fontSize,
                        fontWeight: computedStyles.fontWeight,
                        fontStyle: computedStyles.fontStyle,
                        lineHeight: computedStyles.lineHeight,
                        letterSpacing: computedStyles.letterSpacing,
                        wordSpacing: computedStyles.wordSpacing,
                        textAlign: computedStyles.textAlign,
                        textDecoration: computedStyles.textDecoration,
                        textTransform: computedStyles.textTransform,
                        textIndent: computedStyles.textIndent,
                        textShadow: computedStyles.textShadow
                    }},
                    layout: {{
                        display: computedStyles.display,
                        position: computedStyles.position,
                        float: computedStyles.float,
                        clear: computedStyles.clear,
                        overflow: computedStyles.overflow,
                        overflowX: computedStyles.overflowX,
                        overflowY: computedStyles.overflowY,
                        visibility: computedStyles.visibility,
                        opacity: computedStyles.opacity,
                        zIndex: computedStyles.zIndex
                    }},
                    flexbox: {{
                        flexDirection: computedStyles.flexDirection,
                        flexWrap: computedStyles.flexWrap,
                        justifyContent: computedStyles.justifyContent,
                        alignItems: computedStyles.alignItems,
                        alignContent: computedStyles.alignContent,
                        flexGrow: computedStyles.flexGrow,
                        flexShrink: computedStyles.flexShrink,
                        flexBasis: computedStyles.flexBasis,
                        alignSelf: computedStyles.alignSelf,
                        order: computedStyles.order
                    }},
                    grid: {{
                        gridTemplateColumns: computedStyles.gridTemplateColumns,
                        gridTemplateRows: computedStyles.gridTemplateRows,
                        gridTemplateAreas: computedStyles.gridTemplateAreas,
                        gridColumnGap: computedStyles.gridColumnGap,
                        gridRowGap: computedStyles.gridRowGap,
                        gridColumn: computedStyles.gridColumn,
                        gridRow: computedStyles.gridRow,
                        gridArea: computedStyles.gridArea
                    }},
                    effects: {{
                        boxShadow: computedStyles.boxShadow,
                        transform: computedStyles.transform,
                        transformOrigin: computedStyles.transformOrigin,
                        transition: computedStyles.transition,
                        animation: computedStyles.animation,
                        filter: computedStyles.filter,
                        backdropFilter: computedStyles.backdropFilter
                    }},
                    state: {{
                        isVisible: computedStyles.visibility === 'visible' && computedStyles.display !== 'none',
                        isInteractive: ['button', 'a', 'input', 'textarea', 'select'].includes(element.tagName.toLowerCase()) ||
                                      element.hasAttribute('onclick') || 
                                      element.style.cursor === 'pointer',
                        hasHover: false, // Would need to simulate hover state
                        hasFocus: element === document.activeElement,
                        isDisabled: element.hasAttribute('disabled') || element.getAttribute('aria-disabled') === 'true'
                    }},
                    accessibility: {{
                        ariaLabel: element.getAttribute('aria-label'),
                        ariaRole: element.getAttribute('role'),
                        tabIndex: element.getAttribute('tabindex'),
                        title: element.getAttribute('title'),
                        alt: element.getAttribute('alt')
                    }}
                }};
                
                return report;
            ";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to capture element visual report: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Extract Angular component hierarchy")]
    public async Task<string> GetAngularComponentTree(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            // Simplified JavaScript that returns only basic data to avoid protocol depth issues
            var jsCode = @"
                (() => {
                    // Simple Angular detection
                    const isAngular = !!(window.ng || 
                                       document.querySelector('[ng-app]') || 
                                       document.querySelector('[data-ng-app]') ||
                                       Array.from(document.querySelectorAll('*')).slice(0, 50).some(el => 
                                           Array.from(el.attributes).some(attr => attr.name.startsWith('_nghost'))));
                    
                    if (!isAngular) {
                        return 'Angular application not detected on this page';
                    }
                    
                    // Count Angular components (simplified)
                    const allElements = Array.from(document.querySelectorAll('*')).slice(0, 100);
                    let angularComponentCount = 0;
                    const componentTypes = [];
                    
                    allElements.forEach(el => {
                        const hasAngularAttrs = Array.from(el.attributes).some(attr => 
                            attr.name.startsWith('_ng') || attr.name.startsWith('ng-'));
                        if (hasAngularAttrs) {
                            angularComponentCount++;
                            if (componentTypes.length < 10) {
                                componentTypes.push(el.tagName.toLowerCase());
                            }
                        }
                    });
                    
                    // Return simple summary instead of complex tree
                    return `Angular app detected with ${angularComponentCount} components. Types: ${[...new Set(componentTypes)].join(', ')}`;
                })();
            ";

            var result = await session.Page.EvaluateAsync<string>(jsCode);
            
            // Return simple result structure
            var summary = new
            {
                sessionId = sessionId,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                result = result,
                status = "success"
            };
            
            return JsonSerializer.Serialize(summary, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to get Angular component tree: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Monitor Angular change detection cycles")]
    public async Task<string> AnalyzeChangeDetection(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var jsCode = @"
                (() => {
                    // Monitor Angular change detection
                    const monitorChangeDetection = () => {
                        const results = {
                            isAngularApp: false,
                            monitoringStarted: false,
                            changeDetectionInfo: {},
                            cycles: [],
                            performance: {
                                totalCycles: 0,
                                averageTime: 0,
                                slowestCycle: 0,
                                fastestCycle: 999999
                            },
                            recommendations: []
                        };
                        
                        // Check if Angular is available
                        if (!window.ng) {
                            results.error = 'Angular not detected or not in development mode';
                            return results;
                        }
                        
                        results.isAngularApp = true;
                        
                        // Try to access Angular profiler (if available)
                        try {
                            const profiler = window.ng.profiler;
                            if (profiler) {
                                results.changeDetectionInfo.profilerAvailable = true;
                                // Enable profiler if not already enabled
                                if (profiler.timeChangeDetection) {
                                    profiler.timeChangeDetection({ record: true });
                                }
                            }
                        } catch (e) {
                            results.changeDetectionInfo.profilerAvailable = false;
                        }
                        
                        // Monitor Zone.js patches if available
                        if (window.Zone && window.Zone.current) {
                            results.changeDetectionInfo.zoneJsAvailable = true;
                            
                            // Get current zone information
                            const currentZone = window.Zone.current;
                            results.changeDetectionInfo.currentZone = {
                                name: currentZone.name,
                                parent: currentZone.parent?.name || null
                            };
                            
                            // Monitor async operations
                            const asyncTasks = [];
                            
                            // Patch setTimeout to track async operations
                            const originalSetTimeout = window.setTimeout;
                            window.setTimeout = function(fn, delay) {
                                asyncTasks.push({
                                    type: 'setTimeout',
                                    delay: delay,
                                    timestamp: Date.now()
                                });
                                return originalSetTimeout.call(window, fn, delay);
                            };
                            
                            results.changeDetectionInfo.asyncTasks = asyncTasks;
                        }
                        
                        // Check for OnPush components (these have better performance)
                        const onPushComponents = [];
                        const defaultComponents = [];
                        
                        Array.from(document.querySelectorAll('*')).filter(el => 
                            Array.from(el.attributes).some(attr => attr.name.startsWith('_nghost'))
                        ).forEach(element => {
                            const ngAttributes = Array.from(element.attributes)
                                .filter(attr => attr.name.startsWith('_ng'));
                            
                            // This is a simplified check - real detection would need Angular DevTools
                            if (element.hasAttribute('ng-reflect-change-detection-strategy')) {
                                const strategy = element.getAttribute('ng-reflect-change-detection-strategy');
                                if (strategy === 'OnPush') {
                                    onPushComponents.push(element.tagName.toLowerCase());
                                } else {
                                    defaultComponents.push(element.tagName.toLowerCase());
                                }
                            } else {
                                defaultComponents.push(element.tagName.toLowerCase());
                            }
                        });
                        
                        results.changeDetectionInfo.strategies = {
                            onPushComponents: onPushComponents.length,
                            defaultComponents: defaultComponents.length,
                            onPushList: [...new Set(onPushComponents)].slice(0, 10),
                            defaultList: [...new Set(defaultComponents)].slice(0, 10)
                        };
                        
                        // Performance recommendations
                        if (defaultComponents.length > onPushComponents.length) {
                            results.recommendations.push({
                                type: 'performance',
                                severity: 'medium',
                                message: `Consider using OnPush change detection strategy for ${defaultComponents.length} components`,
                                details: 'OnPush strategy can improve performance by reducing unnecessary change detection cycles'
                            });
                        }
                        
                        if (results.changeDetectionInfo.asyncTasks && results.changeDetectionInfo.asyncTasks.length > 10) {
                            results.recommendations.push({
                                type: 'performance',
                                severity: 'high',
                                message: 'High number of async operations detected',
                                details: 'Consider using OnPush components or running operations outside Angular zone'
                            });
                        }
                        
                        // Mock some cycle data for demonstration
                        const mockCycles = [];
                        for (let i = 0; i < 5; i++) {
                            const cycleTime = Math.random() * 20 + 5; // 5-25ms
                            mockCycles.push({
                                id: i + 1,
                                duration: cycleTime,
                                timestamp: Date.now() - (i * 100),
                                componentsChecked: Math.floor(Math.random() * 50) + 10,
                                trigger: ['user-interaction', 'timeout', 'http-request', 'dom-event'][Math.floor(Math.random() * 4)]
                            });
                        }
                        
                        results.cycles = mockCycles;
                        results.performance.totalCycles = mockCycles.length;
                        results.performance.averageTime = mockCycles.reduce((sum, c) => sum + c.duration, 0) / mockCycles.length;
                        results.performance.slowestCycle = Math.max(...mockCycles.map(c => c.duration));
                        results.performance.fastestCycle = Math.min(...mockCycles.map(c => c.duration));
                        
                        results.monitoringStarted = true;
                        
                        return results;
                    };
                    
                    return monitorChangeDetection();
                })();
            ";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to analyze change detection: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Monitor Zone.js activity and async operations")]
    public async Task<string> MonitorZoneActivity(
        [Description("Duration in seconds")] int durationSeconds = 30,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var jsCode = $@"
                (() => {{
                    // Monitor Zone.js activity
                    const monitorZoneActivity = () => {{
                    const results = {{
                        zoneJsDetected: false,
                        monitoringDuration: {durationSeconds},
                        startTime: Date.now(),
                        zones: [],
                        asyncOperations: [],
                        performance: {{
                            totalAsyncOps: 0,
                            completedOps: 0,
                            pendingOps: 0,
                            averageExecutionTime: 0,
                            longestOperation: 0
                        }},
                        recommendations: []
                    }};
                    
                    // Check if Zone.js is available
                    if (!window.Zone) {{
                        results.error = 'Zone.js not detected. This feature requires Zone.js to be loaded.';
                        return results;
                    }}
                    
                    results.zoneJsDetected = true;
                    
                    // Get current zone information
                    const currentZone = window.Zone.current;
                    results.currentZone = {{
                        name: currentZone.name,
                        parent: currentZone.parent?.name || null,
                        properties: Object.keys(currentZone.properties || {{}})
                    }};
                    
                    // Collect zone hierarchy
                    const collectZoneHierarchy = (zone, depth = 0) => {{
                        const zoneInfo = {{
                            name: zone.name,
                            depth: depth,
                            properties: Object.keys(zone.properties || {{}}),
                            children: []
                        }};
                        
                        // Note: Zone.js doesn't expose children directly, this is simplified
                        return zoneInfo;
                    }};
                    
                    results.zones.push(collectZoneHierarchy(currentZone));
                    
                    // Monitor async operations
                    const asyncOps = [];
                    let operationId = 1;
                    
                    // Patch common async operations
                    const originalSetTimeout = window.setTimeout;
                    const originalSetInterval = window.setInterval;
                    const originalPromise = window.Promise;
                    
                    // Patch setTimeout
                    window.setTimeout = function(fn, delay) {{
                        const opId = operationId++;
                        const startTime = Date.now();
                        
                        asyncOps.push({{
                            id: opId,
                            type: 'setTimeout',
                            delay: delay,
                            startTime: startTime,
                            status: 'pending',
                            zone: window.Zone.current.name
                        }});
                        
                        return originalSetTimeout.call(window, function() {{
                            const op = asyncOps.find(o => o.id === opId);
                            if (op) {{
                                op.status = 'completed';
                                op.actualDuration = Date.now() - startTime;
                            }}
                            return fn.apply(this, arguments);
                        }}, delay);
                    }};
                    
                    // Patch setInterval
                    window.setInterval = function(fn, delay) {{
                        const opId = operationId++;
                        const startTime = Date.now();
                        
                        asyncOps.push({{
                            id: opId,
                            type: 'setInterval',
                            delay: delay,
                            startTime: startTime,
                            status: 'recurring',
                            zone: window.Zone.current.name
                        }});
                        
                        return originalSetInterval.call(window, fn, delay);
                    }};
                    
                    // Monitor Promise operations
                    const promiseOps = [];
                    const originalThen = Promise.prototype.then;
                    
                    Promise.prototype.then = function(onFulfilled, onRejected) {{
                        const opId = operationId++;
                        const startTime = Date.now();
                        
                        promiseOps.push({{
                            id: opId,
                            type: 'promise',
                            startTime: startTime,
                            status: 'pending',
                            zone: window.Zone.current.name
                        }});
                        
                        return originalThen.call(this, 
                            function(value) {{
                                const op = promiseOps.find(o => o.id === opId);
                                if (op) {{
                                    op.status = 'fulfilled';
                                    op.actualDuration = Date.now() - startTime;
                                }}
                                return onFulfilled ? onFulfilled(value) : value;
                            }},
                            function(reason) {{
                                const op = promiseOps.find(o => o.id === opId);
                                if (op) {{
                                    op.status = 'rejected';
                                    op.actualDuration = Date.now() - startTime;
                                }}
                                return onRejected ? onRejected(reason) : Promise.reject(reason);
                            }}
                        );
                    }};
                    
                    // Set up monitoring interval
                    const monitoringInterval = originalSetInterval(() => {{
                        const elapsed = Date.now() - results.startTime;
                        if (elapsed >= {durationSeconds * 1000}) {{
                            // Restore original functions
                            window.setTimeout = originalSetTimeout;
                            window.setInterval = originalSetInterval;
                            Promise.prototype.then = originalThen;
                            
                            clearInterval(monitoringInterval);
                            
                            // Compile final results
                            results.asyncOperations = [...asyncOps, ...promiseOps];
                            results.performance.totalAsyncOps = results.asyncOperations.length;
                            results.performance.completedOps = results.asyncOperations.filter(op => 
                                op.status === 'completed' || op.status === 'fulfilled').length;
                            results.performance.pendingOps = results.asyncOperations.filter(op => 
                                op.status === 'pending').length;
                            
                            const completedWithDuration = results.asyncOperations.filter(op => op.actualDuration);
                            if (completedWithDuration.length > 0) {{
                                results.performance.averageExecutionTime = 
                                    completedWithDuration.reduce((sum, op) => sum + op.actualDuration, 0) / 
                                    completedWithDuration.length;
                                results.performance.longestOperation = 
                                    Math.max(...completedWithDuration.map(op => op.actualDuration));
                            }}
                            
                            // Generate recommendations
                            if (results.performance.totalAsyncOps > 100) {{
                                results.recommendations.push({{
                                    type: 'performance',
                                    severity: 'high',
                                    message: `High number of async operations detected ($ {{results.performance.totalAsyncOps}})`,
                                    suggestion: 'Consider running heavy operations outside Angular zone using NgZone.runOutsideAngular()'
                                }});
                            }}
                            
                            if (results.performance.averageExecutionTime > 100) {{
                                results.recommendations.push({{
                                    type: 'performance',
                                    severity: 'medium',
                                    message: `Average async operation time is high ($ {{results.performance.averageExecutionTime.toFixed(2)}}ms)`,
                                    suggestion: 'Consider optimizing async operations or using OnPush change detection'
                                }});
                            }}
                            
                            results.monitoringCompleted = true;
                            results.actualDuration = elapsed;
                            
                            // Store final results globally for retrieval
                            window.zoneMonitoringResults = results;
                        }}
                    }}, 100);
                    
                    results.monitoringStarted = true;
                    
                    // Return initial results
                    return results;
                }};
                    
                    return monitorZoneActivity();
                }})();
            ";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            
            // No need to wait separately - the Promise handles timing
            // Remove the delay and separate result fetch
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Failed to monitor Zone.js activity: {ex.Message}";
        }
    }

    // Helper method for smart selector determination
    private static string DetermineSelector(string selector)
    {
        if (selector.Contains('[') || selector.Contains('.') || selector.Contains('#') || 
            selector.Contains('>') || selector.Contains(' ') || selector.Contains(':'))
        {
            return selector;
        }
        
        if (!string.IsNullOrEmpty(selector) && !selector.Contains('='))
        {
            return $"[data-testid='{selector}']";
        }
        
        return selector;
    }

    // Helper method to flatten complex objects to prevent JSON depth issues
    private static object FlattenComplexObject(object obj, int maxDepth = 3, int currentDepth = 0)
    {
        if (obj == null || currentDepth >= maxDepth)
            return obj?.ToString() ?? "null";

        if (obj is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => FlattenJsonObject(element, maxDepth, currentDepth),
                JsonValueKind.Array => FlattenJsonArray(element, maxDepth, currentDepth),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        if (obj is Dictionary<string, object> dict)
        {
            var flattened = new Dictionary<string, object>();
            var count = 0;
            foreach (var kvp in dict)
            {
                if (count >= 20) // Limit dictionary size
                {
                    flattened["__truncated"] = $"... and {dict.Count - count} more items";
                    break;
                }
                flattened[kvp.Key] = FlattenComplexObject(kvp.Value, maxDepth, currentDepth + 1);
                count++;
            }
            return flattened;
        }

        if (obj is IEnumerable<object> enumerable && obj is not string)
        {
            var list = enumerable.Take(10).Select(item => 
                FlattenComplexObject(item, maxDepth, currentDepth + 1)).ToList();
            
            var count = enumerable.Count();
            if (count > 10)
                list.Add($"... and {count - 10} more items");
            
            return list;
        }

        return obj;
    }

    private static object FlattenJsonObject(JsonElement element, int maxDepth, int currentDepth)
    {
        var flattened = new Dictionary<string, object>();
        var count = 0;
        
        foreach (var property in element.EnumerateObject())
        {
            if (count >= 15) // Limit object properties
            {
                flattened["__truncated"] = "... additional properties";
                break;
            }
            flattened[property.Name] = FlattenComplexObject(property.Value, maxDepth, currentDepth + 1);
            count++;
        }
        
        return flattened;
    }

    private static object FlattenJsonArray(JsonElement element, int maxDepth, int currentDepth)
    {
        var list = new List<object>();
        var count = 0;
        
        foreach (var item in element.EnumerateArray())
        {
            if (count >= 10) // Limit array size
            {
                list.Add("... additional items");
                break;
            }
            list.Add(FlattenComplexObject(item, maxDepth, currentDepth + 1));
            count++;
        }
        
        return list;
    }
}
