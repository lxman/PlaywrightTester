using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class InteractionTestingTools(PlaywrightSessionManager sessionManager)
{
    [McpServerTool]
    [Description("Drag and drop between elements")]
    public async Task<string> DragAndDrop(
        [Description("Source element selector")] string sourceSelector,
        [Description("Target element selector")] string targetSelector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSourceSelector = DetermineSelector(sourceSelector);
            var finalTargetSelector = DetermineSelector(targetSelector);
            
            var sourceElement = session.Page.Locator(finalSourceSelector);
            var targetElement = session.Page.Locator(finalTargetSelector);
            
            // Check if elements exist
            var sourceCount = await sourceElement.CountAsync();
            var targetCount = await targetElement.CountAsync();
            
            if (sourceCount == 0)
            {
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = "Source element not found", 
                    selector = finalSourceSelector 
                });
            }
            
            if (targetCount == 0)
            {
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = "Target element not found", 
                    selector = finalTargetSelector 
                });
            }

            // Get element information before drag and drop
            var elementInfo = await session.Page.EvaluateAsync<object>($@"
                (() => {{
                    const source = document.querySelector('{finalSourceSelector.Replace("'", "\\'")}');
                    const target = document.querySelector('{finalTargetSelector.Replace("'", "\\'")}');
                    
                    if (!source || !target) return {{ error: 'Elements not found' }};
                    
                    const sourceRect = source.getBoundingClientRect();
                    const targetRect = target.getBoundingClientRect();
                    
                    return {{
                        source: {{
                            tagName: source.tagName.toLowerCase(),
                            className: source.className,
                            id: source.id || null,
                            position: {{
                                x: sourceRect.left + sourceRect.width / 2,
                                y: sourceRect.top + sourceRect.height / 2
                            }},
                            dimensions: {{
                                width: sourceRect.width,
                                height: sourceRect.height
                            }},
                            draggable: source.draggable,
                            hasDataTransfer: !!source.ondragstart
                        }},
                        target: {{
                            tagName: target.tagName.toLowerCase(),
                            className: target.className,
                            id: target.id || null,
                            position: {{
                                x: targetRect.left + targetRect.width / 2,
                                y: targetRect.top + targetRect.height / 2
                            }},
                            dimensions: {{
                                width: targetRect.width,
                                height: targetRect.height
                            }},
                            hasDropHandler: !!target.ondrop
                        }}
                    }};
                }})()
            ");

            // Perform drag and drop
            await sourceElement.DragToAsync(targetElement);
            
            // Wait for any resulting actions
            await Task.Delay(500);
            
            // Check for any changes after drag and drop
            var postDragInfo = await session.Page.EvaluateAsync<object>($@"
                (() => {{
                    const source = document.querySelector('{finalSourceSelector.Replace("'", "\\'")}');
                    const target = document.querySelector('{finalTargetSelector.Replace("'", "\\'")}');
                    
                    if (!source || !target) return {{ error: 'Elements not found after drag' }};
                    
                    const sourceRect = source.getBoundingClientRect();
                    const targetRect = target.getBoundingClientRect();
                    
                    return {{
                        sourcePosition: {{
                            x: sourceRect.left + sourceRect.width / 2,
                            y: sourceRect.top + sourceRect.height / 2
                        }},
                        targetPosition: {{
                            x: targetRect.left + targetRect.width / 2,
                            y: targetRect.top + targetRect.height / 2
                        }},
                        pageChanged: performance.now()
                    }};
                }})()
            ");

            var result = new
            {
                success = true,
                sourceSelector = finalSourceSelector,
                targetSelector = finalTargetSelector,
                preDragInfo = elementInfo,
                postDragInfo = postDragInfo,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to perform drag and drop: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Send keyboard shortcuts like Ctrl+S, Tab, complex sequences, and platform-specific shortcuts")]
    public async Task<string> SendKeyboardShortcut(
        [Description("Keyboard shortcut (e.g., 'Ctrl+S', 'Alt+Tab', 'Cmd+C' for Mac, 'Ctrl+Shift+I', or complex sequences like 'Tab Tab Enter')")] string keys,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            // Detect platform
            var platform = Environment.OSVersion.Platform;
            var isMac = platform == PlatformID.MacOSX || Environment.OSVersion.VersionString.Contains("Darwin");
            
            // Parse and normalize keyboard shortcuts
            var normalizedKeys = NormalizeKeyboardShortcut(keys, isMac);
            var keySequences = ParseKeySequence(normalizedKeys);
            
            // Get initial page state
            var initialState = await session.Page.EvaluateAsync<object>(@"
                (() => {
                    return {
                        activeElement: document.activeElement ? {
                            tagName: document.activeElement.tagName.toLowerCase(),
                            className: document.activeElement.className,
                            id: document.activeElement.id || null,
                            type: document.activeElement.type || null
                        } : null,
                        url: window.location.href,
                        title: document.title
                    };
                })()
            ");

            var executedSequences = new List<object>();
            
            // Execute each key sequence
            foreach (var sequence in keySequences)
            {
                var sequenceStart = DateTime.Now;
                
                if (sequence.IsModifierCombo)
                {
                    // Handle modifier combinations (Ctrl+S, Alt+Tab, etc.)
                    var modifiers = new List<string>();
                    if (sequence.Ctrl) modifiers.Add("Control");
                    if (sequence.Alt) modifiers.Add("Alt");
                    if (sequence.Shift) modifiers.Add("Shift");
                    if (sequence.Meta) modifiers.Add("Meta");
                    
                    await session.Page.Keyboard.PressAsync($"{string.Join("+", modifiers)}+{sequence.Key}");
                }
                else
                {
                    // Handle individual keys or special sequences
                    switch (sequence.Key.ToLower())
                    {
                        case "tab":
                            await session.Page.Keyboard.PressAsync("Tab");
                            break;
                        case "enter":
                        case "return":
                            await session.Page.Keyboard.PressAsync("Enter");
                            break;
                        case "escape":
                        case "esc":
                            await session.Page.Keyboard.PressAsync("Escape");
                            break;
                        case "space":
                            await session.Page.Keyboard.PressAsync("Space");
                            break;
                        case "backspace":
                            await session.Page.Keyboard.PressAsync("Backspace");
                            break;
                        case "delete":
                            await session.Page.Keyboard.PressAsync("Delete");
                            break;
                        case "arrowup":
                        case "up":
                            await session.Page.Keyboard.PressAsync("ArrowUp");
                            break;
                        case "arrowdown":
                        case "down":
                            await session.Page.Keyboard.PressAsync("ArrowDown");
                            break;
                        case "arrowleft":
                        case "left":
                            await session.Page.Keyboard.PressAsync("ArrowLeft");
                            break;
                        case "arrowright":
                        case "right":
                            await session.Page.Keyboard.PressAsync("ArrowRight");
                            break;
                        case "home":
                            await session.Page.Keyboard.PressAsync("Home");
                            break;
                        case "end":
                            await session.Page.Keyboard.PressAsync("End");
                            break;
                        case "pageup":
                            await session.Page.Keyboard.PressAsync("PageUp");
                            break;
                        case "pagedown":
                            await session.Page.Keyboard.PressAsync("PageDown");
                            break;
                        default:
                            // Handle function keys, letters, numbers
                            if (sequence.Key.StartsWith("F") && int.TryParse(sequence.Key[1..], out var fNum) && fNum >= 1 && fNum <= 12)
                            {
                                await session.Page.Keyboard.PressAsync(sequence.Key);
                            }
                            else if (sequence.Key.Length == 1)
                            {
                                await session.Page.Keyboard.PressAsync(sequence.Key);
                            }
                            else
                            {
                                // Try as-is for other keys
                                await session.Page.Keyboard.PressAsync(sequence.Key);
                            }
                            break;
                    }
                }
                
                var sequenceEnd = DateTime.Now;
                executedSequences.Add(new
                {
                    sequence = sequence.Key,
                    modifiers = new { ctrl = sequence.Ctrl, alt = sequence.Alt, shift = sequence.Shift, meta = sequence.Meta },
                    duration = (sequenceEnd - sequenceStart).TotalMilliseconds,
                    timestamp = sequenceEnd.ToString("HH:mm:ss.fff")
                });
                
                // Small delay between sequences for complex sequences
                if (keySequences.Count > 1)
                {
                    await Task.Delay(50);
                }
            }
            
            // Wait for any effects to take place
            await Task.Delay(200);
            
            // Get final page state
            var finalState = await session.Page.EvaluateAsync<object>(@"
                (() => {
                    return {
                        activeElement: document.activeElement ? {
                            tagName: document.activeElement.tagName.toLowerCase(),
                            className: document.activeElement.className,
                            id: document.activeElement.id || null,
                            type: document.activeElement.type || null
                        } : null,
                        url: window.location.href,
                        title: document.title
                    };
                })()
            ");

            var result = new
            {
                success = true,
                originalKeys = keys,
                normalizedKeys = normalizedKeys,
                platform = isMac ? "macOS" : "Windows/Linux",
                executedSequences = executedSequences,
                initialState = initialState,
                finalState = finalState,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to send keyboard shortcut: {ex.Message}";
        }
    }

    // Helper classes for keyboard shortcut parsing
    private class KeySequence
    {
        public string Key { get; set; } = "";
        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Meta { get; set; }
        public bool IsModifierCombo => Ctrl || Alt || Shift || Meta;
    }
    
    private static string NormalizeKeyboardShortcut(string keys, bool isMac)
    {
        var normalized = keys.Trim();
        
        // Platform-specific substitutions
        if (isMac)
        {
            normalized = normalized.Replace("Ctrl+", "Meta+")
                                 .Replace("ctrl+", "meta+")
                                 .Replace("Cmd+", "Meta+")
                                 .Replace("cmd+", "meta+")
                                 .Replace("Command+", "Meta+")
                                 .Replace("command+", "meta+");
        }
        else
        {
            normalized = normalized.Replace("Cmd+", "Ctrl+")
                                 .Replace("cmd+", "ctrl+")
                                 .Replace("Command+", "Ctrl+")
                                 .Replace("command+", "ctrl+")
                                 .Replace("Meta+", "Ctrl+")
                                 .Replace("meta+", "ctrl+");
        }
        
        return normalized;
    }
    
    private static List<KeySequence> ParseKeySequence(string keys)
    {
        var sequences = new List<KeySequence>();
        var parts = keys.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var sequence = new KeySequence();
            var keyParts = part.Split('+');
            
            // Parse modifiers and key
            for (int i = 0; i < keyParts.Length; i++)
            {
                var keyPart = keyParts[i].Trim();
                
                if (i == keyParts.Length - 1)
                {
                    // Last part is the actual key
                    sequence.Key = keyPart;
                }
                else
                {
                    // Modifier keys
                    switch (keyPart.ToLower())
                    {
                        case "ctrl":
                        case "control":
                            sequence.Ctrl = true;
                            break;
                        case "alt":
                            sequence.Alt = true;
                            break;
                        case "shift":
                            sequence.Shift = true;
                            break;
                        case "meta":
                        case "cmd":
                        case "command":
                            sequence.Meta = true;
                            break;
                    }
                }
            }
            
            sequences.Add(sequence);
        }
        
        return sequences;
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
}