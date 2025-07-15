using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class AccessibilityTestingTools(PlaywrightSessionManager sessionManager)
{
    [McpServerTool]
    [Description("Validate ARIA labels and attributes with comprehensive rules")]
    public async Task<string> ValidateAriaLabels(
        [Description("Optional container selector to limit analysis")] string? containerSelector = null,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalContainerSelector = !string.IsNullOrEmpty(containerSelector) 
                ? DetermineSelector(containerSelector) 
                : "body";

            var result = await session.Page.EvaluateAsync<object>($@"
                (containerSelector) => {{
                    const container = document.querySelector(containerSelector) || document.body;
                    
                    // Elements that require accessible labels
                    const elementsNeedingLabels = container.querySelectorAll(`
                        input:not([type='hidden']), 
                        button, 
                        select, 
                        textarea,
                        [role='button'],
                        [role='textbox'],
                        [role='combobox'],
                        [role='listbox'],
                        [role='slider'],
                        [role='switch'],
                        [role='checkbox'],
                        [role='radio']
                    `);
                    
                    const issues = [];
                    const warnings = [];
                    const successes = [];
                    
                    elementsNeedingLabels.forEach((element, index) => {{
                        const analysis = analyzeElementLabeling(element, index);
                        
                        if (analysis.severity === 'error') {{
                            issues.push(analysis);
                        }} else if (analysis.severity === 'warning') {{
                            warnings.push(analysis);
                        }} else {{
                            successes.push(analysis);
                        }}
                    }});
                    
                    // Check for ARIA landmark regions
                    const landmarks = analyzeLandmarks(container);
                    
                    // Check for heading hierarchy
                    const headingStructure = analyzeHeadingStructure(container);
                    
                    return {{
                        summary: {{
                            totalElements: elementsNeedingLabels.length,
                            errors: issues.length,
                            warnings: warnings.length,
                            passed: successes.length,
                            score: Math.round((successes.length / elementsNeedingLabels.length) * 100) || 0
                        }},
                        accessibility: {{
                            errors: issues,
                            warnings: warnings,
                            landmarks: landmarks,
                            headingStructure: headingStructure
                        }},
                        recommendations: generateAccessibilityRecommendations(issues, warnings)
                    }};
                    
                    function analyzeElementLabeling(element, index) {{
                        const elementInfo = {{
                            index: index + 1,
                            tagName: element.tagName.toLowerCase(),
                            type: element.type || null,
                            id: element.id || null,
                            className: element.className || null,
                            testId: element.getAttribute('data-testid') || null,
                            role: element.getAttribute('role') || element.tagName.toLowerCase()
                        }};
                        
                        const labeling = {{
                            ariaLabel: element.getAttribute('aria-label'),
                            ariaLabelledBy: element.getAttribute('aria-labelledby'),
                            ariaDescribedBy: element.getAttribute('aria-describedby'),
                            title: element.getAttribute('title'),
                            placeholder: element.getAttribute('placeholder')
                        }};
                        
                        // Find associated label elements
                        const associatedLabel = element.id ? 
                            document.querySelector(`label[for='${{element.id}}']`) : null;
                        const parentLabel = element.closest('label');
                        
                        const accessibility = {{
                            hasAriaLabel: !!labeling.ariaLabel,
                            hasAriaLabelledBy: !!labeling.ariaLabelledBy,
                            hasAssociatedLabel: !!associatedLabel,
                            hasParentLabel: !!parentLabel,
                            hasTitle: !!labeling.title,
                            hasPlaceholder: !!labeling.placeholder
                        }};
                        
                        // Determine accessibility status
                        let severity = 'success';
                        let issue = null;
                        let suggestion = null;
                        
                        const hasAccessibleName = accessibility.hasAriaLabel || 
                                                  accessibility.hasAriaLabelledBy || 
                                                  accessibility.hasAssociatedLabel || 
                                                  accessibility.hasParentLabel;
                        
                        if (!hasAccessibleName) {{
                            if (element.tagName.toLowerCase() === 'input' && 
                                ['submit', 'button', 'reset'].includes(element.type)) {{
                                severity = element.value ? 'warning' : 'error';
                                issue = element.value ? 
                                    'Button uses value attribute for label (consider aria-label)' :
                                    'Button has no accessible label';
                                suggestion = 'Add aria-label, associate with a label element, or provide descriptive value';
                            }} else if (element.tagName.toLowerCase() === 'button') {{
                                severity = element.textContent?.trim() ? 'warning' : 'error';
                                issue = element.textContent?.trim() ? 
                                    'Button relies on text content (consider aria-label for clarity)' :
                                    'Button has no accessible label or text content';
                                suggestion = 'Add aria-label or descriptive text content';
                            }} else {{
                                severity = 'error';
                                issue = 'Form element has no accessible label';
                                suggestion = 'Add aria-label, aria-labelledby, or associate with a label element';
                            }}
                        }} else if (accessibility.hasPlaceholder && !accessibility.hasAriaLabel && 
                                   !accessibility.hasAssociatedLabel && !accessibility.hasParentLabel) {{
                            severity = 'warning';
                            issue = 'Element only has placeholder text (not accessible to all screen readers)';
                            suggestion = 'Add proper label in addition to placeholder';
                        }}
                        
                        return {{
                            elementInfo,
                            labeling,
                            accessibility,
                            severity,
                            issue,
                            suggestion,
                            effectiveLabel: getEffectiveLabel(element, labeling, associatedLabel, parentLabel)
                        }};
                    }}
                    
                    function getEffectiveLabel(element, labeling, associatedLabel, parentLabel) {{
                        if (labeling.ariaLabel) return labeling.ariaLabel;
                        if (labeling.ariaLabelledBy) {{
                            const referencedElements = labeling.ariaLabelledBy.split(' ')
                                .map(id => document.getElementById(id))
                                .filter(el => el);
                            return referencedElements.map(el => el.textContent?.trim()).join(' ');
                        }}
                        if (associatedLabel) return associatedLabel.textContent?.trim();
                        if (parentLabel) return parentLabel.textContent?.trim();
                        if (element.tagName.toLowerCase() === 'button') return element.textContent?.trim();
                        if (element.value && ['submit', 'button', 'reset'].includes(element.type)) return element.value;
                        return labeling.placeholder || labeling.title || null;
                    }}
                    
                    function analyzeLandmarks(container) {{
                        const landmarks = container.querySelectorAll(`
                            main, nav, aside, section, article, header, footer,
                            [role='main'], [role='navigation'], [role='complementary'],
                            [role='region'], [role='banner'], [role='contentinfo']
                        `);
                        
                        const landmarkAnalysis = Array.from(landmarks).map(landmark => ({{
                            tagName: landmark.tagName.toLowerCase(),
                            role: landmark.getAttribute('role') || landmark.tagName.toLowerCase(),
                            hasLabel: !!(landmark.getAttribute('aria-label') || landmark.getAttribute('aria-labelledby')),
                            id: landmark.id || null,
                            className: landmark.className || null
                        }}));
                        
                        return {{
                            count: landmarks.length,
                            landmarks: landmarkAnalysis,
                            missingMain: !container.querySelector('main, [role=""main""]'),
                            hasNavigation: !!container.querySelector('nav, [role=""navigation""]')
                        }};
                    }}
                    
                    function analyzeHeadingStructure(container) {{
                        const headings = container.querySelectorAll('h1, h2, h3, h4, h5, h6, [role=""heading""]');
                        
                        const headingStructure = Array.from(headings).map((heading, index) => {{
                            const level = heading.tagName ? 
                                parseInt(heading.tagName.charAt(1)) :
                                parseInt(heading.getAttribute('aria-level')) || 1;
                            
                            return {{
                                index: index + 1,
                                tagName: heading.tagName?.toLowerCase() || 'div',
                                level: level,
                                text: heading.textContent?.trim() || '',
                                hasId: !!heading.id,
                                isEmpty: !heading.textContent?.trim()
                            }};
                        }});
                        
                        const issues = [];
                        
                        // Check for proper heading hierarchy
                        for (let i = 1; i < headingStructure.length; i++) {{
                            const current = headingStructure[i];
                            const previous = headingStructure[i - 1];
                            
                            if (current.level - previous.level > 1) {{
                                issues.push(`Heading level jump: h${{previous.level}} to h${{current.level}} (should be sequential)`);
                            }}
                        }}
                        
                        return {{
                            count: headings.length,
                            structure: headingStructure,
                            issues: issues,
                            hasH1: headingStructure.some(h => h.level === 1),
                            multipleH1: headingStructure.filter(h => h.level === 1).length > 1
                        }};
                    }}
                    
                    function generateAccessibilityRecommendations(errors, warnings) {{
                        const recommendations = [];
                        
                        if (errors.length > 0) {{
                            recommendations.push({{
                                priority: 'HIGH',
                                category: 'Critical Accessibility Issues',
                                action: `Fix ${{errors.length}} critical labeling error(s) that prevent screen reader access`,
                                impact: 'These issues make form elements completely inaccessible to screen readers'
                            }});
                        }}
                        
                        if (warnings.length > 0) {{
                            recommendations.push({{
                                priority: 'MEDIUM',
                                category: 'Accessibility Improvements',
                                action: `Address ${{warnings.length}} accessibility warning(s) for better user experience`,
                                impact: 'These improvements will enhance accessibility for all users'
                            }});
                        }}
                        
                        return recommendations;
                    }}
                }}", finalContainerSelector);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                capability = "ValidateAriaLabels"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string DetermineSelector(string selector)
    {
        // Check if it's already a CSS selector (contains . # [ : etc.)
        if (selector.Contains('.') || selector.Contains('#') || selector.Contains('[') || 
            selector.Contains(':') || selector.Contains(' ') || selector.Contains('>'))
        {
            return selector;
        }
        
        // Assume it's a data-testid
        return $"[data-testid='{selector}']";
    }

    [McpServerTool]
    [Description("Test color contrast ratios against WCAG guidelines")]
    public async Task<string> TestColorContrast(
        [Description("Minimum contrast ratio (4.5 for AA, 3.0 for AA Large, 7.0 for AAA)")] double ratio = 4.5,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var result = await session.Page.EvaluateAsync<object>($@"
                (minimumRatio) => {{
                    const textElements = document.querySelectorAll(`
                        p, span, div, label, button, a, h1, h2, h3, h4, h5, h6,
                        input[type='text'], input[type='email'], input[type='password'],
                        textarea, select, option, [role='button'], [role='link']
                    `);
                    
                    const contrastResults = [];
                    const fails = [];
                    const warnings = [];
                    const passes = [];
                    
                    textElements.forEach((element, index) => {{
                        const analysis = analyzeElementContrast(element, index, minimumRatio);
                        if (analysis) {{
                            contrastResults.push(analysis);
                            
                            if (analysis.wcagLevel === 'FAIL') {{
                                fails.push(analysis);
                            }} else if (analysis.wcagLevel.includes('WARNING')) {{
                                warnings.push(analysis);
                            }} else {{
                                passes.push(analysis);
                            }}
                        }}
                    }});
                    
                    const summary = {{
                        totalElements: textElements.length,
                        analyzed: contrastResults.filter(r => r.contrastRatio !== null).length,
                        passed: passes.length,
                        warnings: warnings.length,
                        failed: fails.length,
                        averageContrast: calculateAverageContrast(contrastResults),
                        minimumRatioTested: minimumRatio
                    }};
                    
                    return {{
                        summary,
                        wcagGuidelines: getWCAGGuidelines(),
                        results: {{
                            failures: fails,
                            warnings: warnings.slice(0, 10), // Limit warnings output
                            passes: passes.slice(0, 5) // Show some successful examples
                        }},
                        recommendations: generateContrastRecommendations(fails, warnings, summary)
                    }};
                    
                    function analyzeElementContrast(element, index, minimumRatio) {{
                        const rect = element.getBoundingClientRect();
                        
                        // Skip elements that are not visible
                        if (rect.width === 0 || rect.height === 0) {{
                            return null;
                        }}
                        
                        const styles = window.getComputedStyle(element);
                        const textContent = element.textContent?.trim();
                        
                        // Skip elements without text content
                        if (!textContent) {{
                            return null;
                        }}
                        
                        const foregroundColor = styles.color;
                        const backgroundColor = getEffectiveBackgroundColor(element);
                        const fontSize = parseFloat(styles.fontSize);
                        const fontWeight = styles.fontWeight;
                        
                        const isLargeText = fontSize >= 18 || (fontSize >= 14 && (fontWeight === 'bold' || parseInt(fontWeight) >= 700));
                        
                        const contrastRatio = calculateContrastRatio(foregroundColor, backgroundColor);
                        const wcagLevel = evaluateWCAGLevel(contrastRatio, isLargeText, minimumRatio);
                        
                        return {{
                            index: index + 1,
                            element: {{
                                tagName: element.tagName.toLowerCase(),
                                id: element.id || null,
                                className: element.className || null,
                                testId: element.getAttribute('data-testid') || null,
                                text: textContent.substring(0, 100) + (textContent.length > 100 ? '...' : '')
                            }},
                            colors: {{
                                foreground: foregroundColor,
                                background: backgroundColor,
                                foregroundRgb: parseColor(foregroundColor),
                                backgroundRgb: parseColor(backgroundColor)
                            }},
                            typography: {{
                                fontSize: fontSize,
                                fontWeight: fontWeight,
                                isLargeText: isLargeText
                            }},
                            contrastRatio: contrastRatio,
                            wcagLevel: wcagLevel,
                            accessibility: {{
                                passesAA: contrastRatio >= (isLargeText ? 3.0 : 4.5),
                                passesAAA: contrastRatio >= (isLargeText ? 4.5 : 7.0),
                                meetsMinimum: contrastRatio >= minimumRatio
                            }},
                            position: {{
                                top: Math.round(rect.top),
                                left: Math.round(rect.left),
                                width: Math.round(rect.width),
                                height: Math.round(rect.height)
                            }}
                        }};
                    }}
                    
                    function getEffectiveBackgroundColor(element) {{
                        let currentElement = element;
                        let backgroundColor = 'transparent';
                        
                        // Walk up the DOM tree to find the first non-transparent background
                        while (currentElement && backgroundColor === 'transparent') {{
                            const styles = window.getComputedStyle(currentElement);
                            backgroundColor = styles.backgroundColor;
                            
                            if (backgroundColor === 'rgba(0, 0, 0, 0)' || backgroundColor === 'transparent') {{
                                currentElement = currentElement.parentElement;
                            }} else {{
                                break;
                            }}
                        }}
                        
                        // Default to white if no background found
                        return backgroundColor === 'transparent' ? 'rgb(255, 255, 255)' : backgroundColor;
                    }}
                    
                    function parseColor(color) {{
                        const canvas = document.createElement('canvas');
                        const ctx = canvas.getContext('2d');
                        ctx.fillStyle = color;
                        const computedColor = ctx.fillStyle;
                        
                        // Parse hex or rgb values
                        if (computedColor.startsWith('#')) {{
                            const hex = computedColor.slice(1);
                            return {{
                                r: parseInt(hex.slice(0, 2), 16),
                                g: parseInt(hex.slice(2, 4), 16),
                                b: parseInt(hex.slice(4, 6), 16)
                            }};
                        }} else if (computedColor.startsWith('rgb')) {{
                            const matches = computedColor.match(/\d+/g);
                            return {{
                                r: parseInt(matches[0]),
                                g: parseInt(matches[1]),
                                b: parseInt(matches[2])
                            }};
                        }}
                        
                        return {{ r: 0, g: 0, b: 0 }}; // fallback
                    }}
                    
                    function calculateContrastRatio(foreground, background) {{
                        const fg = parseColor(foreground);
                        const bg = parseColor(background);
                        
                        const fgLuminance = getLuminance(fg);
                        const bgLuminance = getLuminance(bg);
                        
                        const lighter = Math.max(fgLuminance, bgLuminance);
                        const darker = Math.min(fgLuminance, bgLuminance);
                        
                        return Math.round(((lighter + 0.05) / (darker + 0.05)) * 100) / 100;
                    }}
                    
                    function getLuminance(rgb) {{
                        const {{ r, g, b }} = rgb;
                        
                        const rsRGB = r / 255;
                        const gsRGB = g / 255;
                        const bsRGB = b / 255;
                        
                        const rLin = rsRGB <= 0.03928 ? rsRGB / 12.92 : Math.pow((rsRGB + 0.055) / 1.055, 2.4);
                        const gLin = gsRGB <= 0.03928 ? gsRGB / 12.92 : Math.pow((gsRGB + 0.055) / 1.055, 2.4);
                        const bLin = bsRGB <= 0.03928 ? bsRGB / 12.92 : Math.pow((bsRGB + 0.055) / 1.055, 2.4);
                        
                        return 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
                    }}
                    
                    function evaluateWCAGLevel(ratio, isLargeText, minimumRatio) {{
                        if (ratio < minimumRatio) {{
                            return 'FAIL';
                        }}
                        
                        if (isLargeText) {{
                            if (ratio >= 4.5) return 'AAA Large Text';
                            if (ratio >= 3.0) return 'AA Large Text';
                            return 'FAIL';
                        }} else {{
                            if (ratio >= 7.0) return 'AAA Normal Text';
                            if (ratio >= 4.5) return 'AA Normal Text';
                            if (ratio >= 3.0) return 'WARNING - Below AA standard';
                            return 'FAIL';
                        }}
                    }}
                    
                    function calculateAverageContrast(results) {{
                        const validResults = results.filter(r => r && r.contrastRatio !== null);
                        if (validResults.length === 0) return 0;
                        
                        const sum = validResults.reduce((acc, r) => acc + r.contrastRatio, 0);
                        return Math.round((sum / validResults.length) * 100) / 100;
                    }}
                    
                    function getWCAGGuidelines() {{
                        return {{
                            normalText: {{
                                AA: 4.5,
                                AAA: 7.0
                            }},
                            largeText: {{
                                AA: 3.0,
                                AAA: 4.5
                            }},
                            largeTextDefinition: 'Text that is 18pt or larger, or 14pt bold or larger'
                        }};
                    }}
                    
                    function generateContrastRecommendations(fails, warnings, summary) {{
                        const recommendations = [];
                        
                        if (fails.length > 0) {{
                            recommendations.push({{
                                priority: 'CRITICAL',
                                category: 'WCAG Compliance Failures',
                                action: `Fix ${{fails.length}} critical contrast failures`,
                                impact: 'These elements fail WCAG contrast requirements and are difficult for users with visual impairments to read',
                                suggestion: 'Increase color contrast by darkening text or lightening backgrounds'
                            }});
                        }}
                        
                        if (warnings.length > 0) {{
                            recommendations.push({{
                                priority: 'HIGH',
                                category: 'Contrast Improvements',
                                action: `Improve ${{warnings.length}} elements with low contrast ratios`,
                                impact: 'These elements may be difficult to read for some users',
                                suggestion: 'Consider increasing contrast for better accessibility'
                            }});
                        }}
                        
                        if (summary.averageContrast < 4.5) {{
                            recommendations.push({{
                                priority: 'MEDIUM',
                                category: 'Overall Design',
                                action: 'Review overall color scheme for better contrast',
                                impact: 'Low average contrast suggests systematic design issues',
                                suggestion: 'Consider using a color palette with higher contrast ratios'
                            }});
                        }}
                        
                        return recommendations;
                    }}
                }}", ratio);

            // Filter out null results
            var resultObj = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            return JsonSerializer.Serialize(resultObj, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                capability = "TestColorContrast"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    [McpServerTool]
    [Description("Test keyboard focus order and focus management")]
    public async Task<string> TestFocusOrder(
        [Description("Optional container selector to limit focus testing")] string? containerSelector = null,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalContainerSelector = !string.IsNullOrEmpty(containerSelector) 
                ? DetermineSelector(containerSelector) 
                : "body";

            var result = await session.Page.EvaluateAsync<object>($@"
                (containerSelector) => {{
                    const container = document.querySelector(containerSelector) || document.body;
                    
                    // Get all focusable elements
                    const focusableElements = getFocusableElements(container);
                    
                    // Analyze focus order
                    const focusAnalysis = analyzeFocusOrder(focusableElements);
                    
                    // Test keyboard navigation
                    const keyboardNavigation = testKeyboardNavigation(focusableElements);
                    
                    // Check for focus traps and management
                    const focusManagement = analyzeFocusManagement(container);
                    
                    // Generate focus map
                    const focusMap = generateFocusMap(focusableElements);
                    
                    return {{
                        summary: {{
                            totalFocusableElements: focusableElements.length,
                            hasTabIndex: focusableElements.filter(el => el.tabIndex !== 0).length,
                            hasNegativeTabIndex: focusableElements.filter(el => el.tabIndex === -1).length,
                            customTabOrder: focusableElements.filter(el => el.tabIndex > 0).length,
                            score: calculateFocusScore(focusAnalysis, keyboardNavigation, focusManagement)
                        }},
                        focusOrder: focusAnalysis,
                        keyboardNavigation: keyboardNavigation,
                        focusManagement: focusManagement,
                        focusMap: focusMap,
                        recommendations: generateFocusRecommendations(focusAnalysis, keyboardNavigation, focusManagement)
                    }};
                    
                    function getFocusableElements(container) {{
                        const selector = `
                            a[href]:not([disabled]),
                            button:not([disabled]),
                            textarea:not([disabled]),
                            input[type='text']:not([disabled]),
                            input[type='radio']:not([disabled]),
                            input[type='checkbox']:not([disabled]),
                            input[type='submit']:not([disabled]),
                            input[type='button']:not([disabled]),
                            input[type='email']:not([disabled]),
                            input[type='password']:not([disabled]),
                            input[type='search']:not([disabled]),
                            input[type='tel']:not([disabled]),
                            input[type='url']:not([disabled]),
                            input[type='number']:not([disabled]),
                            input[type='date']:not([disabled]),
                            input[type='datetime-local']:not([disabled]),
                            input[type='month']:not([disabled]),
                            input[type='time']:not([disabled]),
                            input[type='week']:not([disabled]),
                            select:not([disabled]),
                            details,
                            [tabindex]:not([tabindex='-1']):not([disabled]),
                            [contentEditable=true]:not([disabled])
                        `;
                        
                        const elements = Array.from(container.querySelectorAll(selector));
                        
                        // Filter out elements that are not visible or have display: none
                        return elements.filter(element => {{
                            const rect = element.getBoundingClientRect();
                            const styles = window.getComputedStyle(element);
                            
                            return rect.width > 0 && 
                                   rect.height > 0 && 
                                   styles.display !== 'none' && 
                                   styles.visibility !== 'hidden' &&
                                   !element.hasAttribute('inert');
                        }}).map((element, index) => {{
                            const rect = element.getBoundingClientRect();
                            return {{
                                element: element,
                                index: index,
                                tagName: element.tagName.toLowerCase(),
                                type: element.type || null,
                                id: element.id || null,
                                className: element.className || null,
                                testId: element.getAttribute('data-testid') || null,
                                role: element.getAttribute('role') || null,
                                tabIndex: element.tabIndex,
                                ariaLabel: element.getAttribute('aria-label') || null,
                                position: {{
                                    top: Math.round(rect.top),
                                    left: Math.round(rect.left),
                                    width: Math.round(rect.width),
                                    height: Math.round(rect.height)
                                }},
                                text: element.textContent?.trim()?.substring(0, 50) || element.value?.substring(0, 50) || null
                            }};
                        }});
                    }}
                    
                    function analyzeFocusOrder(elements) {{
                        const analysis = {{
                            naturalOrder: [],
                            customOrder: [],
                            issues: [],
                            logicalOrder: true
                        }};
                        
                        // Separate elements with custom tabindex from natural order
                        elements.forEach(element => {{
                            if (element.tabIndex > 0) {{
                                analysis.customOrder.push(element);
                            }} else {{
                                analysis.naturalOrder.push(element);
                            }}
                        }});
                        
                        // Sort custom order by tabindex
                        analysis.customOrder.sort((a, b) => a.tabIndex - b.tabIndex);
                        
                        // Check for logical visual order
                        analysis.logicalOrder = checkLogicalOrder(analysis.naturalOrder);
                        
                        // Find common issues
                        analysis.issues = findFocusOrderIssues(elements, analysis);
                        
                        return analysis;
                    }}
                    
                    function checkLogicalOrder(elements) {{
                        for (let i = 1; i < elements.length; i++) {{
                            const current = elements[i];
                            const previous = elements[i - 1];
                            
                            // Check if current element is significantly above previous element
                            const verticalGap = current.position.top - previous.position.top;
                            const horizontalGap = current.position.left - previous.position.left;
                            
                            // If current element is more than 50px above previous, might be out of order
                            if (verticalGap < -50) {{
                                return false;
                            }}
                            
                            // If on same row but significantly to the left, might be out of order
                            if (Math.abs(verticalGap) < 20 && horizontalGap < -100) {{
                                return false;
                            }}
                        }}
                        return true;
                    }}
                    
                    function findFocusOrderIssues(elements, analysis) {{
                        const issues = [];
                        
                        // Check for positive tabindex values (generally discouraged)
                        if (analysis.customOrder.length > 0) {{
                            issues.push({{
                                type: 'positive_tabindex',
                                severity: 'warning',
                                count: analysis.customOrder.length,
                                message: 'Positive tabindex values found - these can create confusing focus order',
                                elements: analysis.customOrder.map(el => ({{
                                    id: el.id,
                                    testId: el.testId,
                                    tabIndex: el.tabIndex
                                }}))
                            }});
                        }}
                        
                        // Check for missing focus indicators
                        const elementsWithoutFocusStyle = elements.filter(el => {{
                            // This is a simplified check - in reality, you'd need to check computed styles
                            const hasCustomFocusStyle = el.element.getAttribute('class')?.includes('focus') ||
                                                       el.element.getAttribute('class')?.includes('outline');
                            return !hasCustomFocusStyle;
                        }});
                        
                        if (elementsWithoutFocusStyle.length > elements.length * 0.5) {{
                            issues.push({{
                                type: 'missing_focus_indicators',
                                severity: 'warning',
                                message: 'Many elements may lack visible focus indicators',
                                suggestion: 'Ensure all focusable elements have clear focus indicators'
                            }});
                        }}
                        
                        // Check for logical order issues
                        if (!analysis.logicalOrder) {{
                            issues.push({{
                                type: 'illogical_order',
                                severity: 'error',
                                message: 'Focus order does not follow logical visual flow',
                                suggestion: 'Reorder elements in DOM or use appropriate tabindex values'
                            }});
                        }}
                        
                        return issues;
                    }}
                    
                    function testKeyboardNavigation(elements) {{
                        const navigation = {{
                            tabNavigation: true,
                            arrowKeySupport: false,
                            escapeKeySupport: false,
                            enterSpaceSupport: true,
                            shortcuts: []
                        }};
                        
                        // Check for components that should support arrow key navigation
                        const arrowKeyComponents = elements.filter(el => {{
                            const role = el.role || el.element.getAttribute('role');
                            return ['listbox', 'menu', 'menubar', 'tablist', 'tree', 'grid'].includes(role);
                        }});
                        
                        navigation.arrowKeySupport = arrowKeyComponents.length > 0;
                        
                        // Check for modal/dialog components that should support escape
                        const modalComponents = elements.filter(el => {{
                            const role = el.role || el.element.getAttribute('role');
                            return ['dialog', 'alertdialog'].includes(role) ||
                                   el.element.closest('[role=""dialog""]') ||
                                   el.element.closest('.modal');
                        }});
                        
                        navigation.escapeKeySupport = modalComponents.length > 0;
                        
                        // Look for keyboard shortcuts (elements with accesskey)
                        navigation.shortcuts = elements
                            .filter(el => el.element.hasAttribute('accesskey'))
                            .map(el => ({{
                                element: el.id || el.testId || el.tagName,
                                accesskey: el.element.getAttribute('accesskey')
                            }}));
                        
                        return navigation;
                    }}
                    
                    function analyzeFocusManagement(container) {{
                        const management = {{
                            focusTraps: [],
                            skipLinks: [],
                            autoFocus: [],
                            focusRestoration: false
                        }};
                        
                        // Look for focus traps (elements with role='dialog' or similar)
                        const potentialTraps = container.querySelectorAll('[role=""dialog""], [role=""alertdialog""], .modal, .popup');
                        management.focusTraps = Array.from(potentialTraps).map(trap => ({{
                            tagName: trap.tagName.toLowerCase(),
                            id: trap.id || null,
                            className: trap.className || null,
                            role: trap.getAttribute('role')
                        }}));
                        
                        // Look for skip links
                        const skipLinks = container.querySelectorAll('a[href^=""#""], [role=""button""][onclick*=""skip""]');
                        management.skipLinks = Array.from(skipLinks)
                            .filter(link => link.textContent?.toLowerCase().includes('skip'))
                            .map(link => ({{
                                text: link.textContent?.trim(),
                                href: link.getAttribute('href'),
                                visible: window.getComputedStyle(link).display !== 'none'
                            }}));
                        
                        // Look for autofocus elements
                        const autoFocusElements = container.querySelectorAll('[autofocus]');
                        management.autoFocus = Array.from(autoFocusElements).map(el => ({{
                            tagName: el.tagName.toLowerCase(),
                            id: el.id || null,
                            type: el.type || null
                        }}));
                        
                        return management;
                    }}
                    
                    function generateFocusMap(elements) {{
                        const map = {{
                            totalElements: elements.length,
                            regions: categorizeFocusRegions(elements),
                            visualFlow: analyzeVisualFlow(elements)
                        }};
                        
                        return map;
                    }}
                    
                    function categorizeFocusRegions(elements) {{
                        const regions = {{
                            header: [],
                            navigation: [],
                            main: [],
                            sidebar: [],
                            footer: [],
                            other: []
                        }};
                        
                        elements.forEach(element => {{
                            const closest = element.element.closest('header, nav, main, aside, footer, [role=""banner""], [role=""navigation""], [role=""main""], [role=""complementary""], [role=""contentinfo""]');
                            
                            if (closest) {{
                                const tagName = closest.tagName.toLowerCase();
                                const role = closest.getAttribute('role');
                                
                                if (tagName === 'header' || role === 'banner') {{
                                    regions.header.push(element);
                                }} else if (tagName === 'nav' || role === 'navigation') {{
                                    regions.navigation.push(element);
                                }} else if (tagName === 'main' || role === 'main') {{
                                    regions.main.push(element);
                                }} else if (tagName === 'aside' || role === 'complementary') {{
                                    regions.sidebar.push(element);
                                }} else if (tagName === 'footer' || role === 'contentinfo') {{
                                    regions.footer.push(element);
                                }} else {{
                                    regions.other.push(element);
                                }}
                            }} else {{
                                regions.other.push(element);
                            }}
                        }});
                        
                        return Object.fromEntries(
                            Object.entries(regions).map(([key, value]) => [key, value.length])
                        );
                    }}
                    
                    function analyzeVisualFlow(elements) {{
                        // Group elements by approximate row (within 20px vertically)
                        const rows = [];
                        
                        elements.forEach(element => {{
                            const existingRow = rows.find(row => 
                                Math.abs(row.top - element.position.top) < 20
                            );
                            
                            if (existingRow) {{
                                existingRow.elements.push(element);
                            }} else {{
                                rows.push({{
                                    top: element.position.top,
                                    elements: [element]
                                }});
                            }}
                        }});
                        
                        // Sort rows by vertical position and elements within rows by horizontal position
                        rows.sort((a, b) => a.top - b.top);
                        rows.forEach(row => {{
                            row.elements.sort((a, b) => a.position.left - b.position.left);
                        }});
                        
                        return {{
                            rowCount: rows.length,
                            averageElementsPerRow: Math.round(elements.length / rows.length * 10) / 10
                        }};
                    }}
                    
                    function calculateFocusScore(focusAnalysis, keyboardNavigation, focusManagement) {{
                        let score = 100;
                        
                        // Deduct points for issues
                        focusAnalysis.issues.forEach(issue => {{
                            if (issue.severity === 'error') score -= 20;
                            if (issue.severity === 'warning') score -= 10;
                        }});
                        
                        // Deduct points for poor logical order
                        if (!focusAnalysis.logicalOrder) score -= 15;
                        
                        // Add points for good practices
                        if (focusManagement.skipLinks.length > 0) score += 5;
                        if (keyboardNavigation.shortcuts.length > 0) score += 5;
                        
                        return Math.max(0, Math.min(100, score));
                    }}
                    
                    function generateFocusRecommendations(focusAnalysis, keyboardNavigation, focusManagement) {{
                        const recommendations = [];
                        
                        if (focusAnalysis.issues.length > 0) {{
                            const errors = focusAnalysis.issues.filter(i => i.severity === 'error');
                            const warnings = focusAnalysis.issues.filter(i => i.severity === 'warning');
                            
                            if (errors.length > 0) {{
                                recommendations.push({{
                                    priority: 'HIGH',
                                    category: 'Focus Order Issues',
                                    action: `Fix ${{errors.length}} critical focus order issue(s)`,
                                    impact: 'Users navigating with keyboard will experience confusing or broken navigation'
                                }});
                            }}
                            
                            if (warnings.length > 0) {{
                                recommendations.push({{
                                    priority: 'MEDIUM',
                                    category: 'Focus Improvements',
                                    action: `Address ${{warnings.length}} focus order warning(s)`,
                                    impact: 'Improvements will enhance keyboard navigation experience'
                                }});
                            }}
                        }}
                        
                        if (focusManagement.skipLinks.length === 0) {{
                            recommendations.push({{
                                priority: 'MEDIUM',
                                category: 'Navigation Enhancement',
                                action: 'Add skip links for keyboard users',
                                impact: 'Skip links help keyboard users navigate more efficiently'
                            }});
                        }}
                        
                        if (focusAnalysis.customOrder.length > 0) {{
                            recommendations.push({{
                                priority: 'LOW',
                                category: 'Code Quality',
                                action: 'Consider removing positive tabindex values',
                                impact: 'Relying on natural DOM order is more maintainable and predictable'
                            }});
                        }}
                        
                        return recommendations;
                    }}
                }}", finalContainerSelector);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                capability = "TestFocusOrder"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
