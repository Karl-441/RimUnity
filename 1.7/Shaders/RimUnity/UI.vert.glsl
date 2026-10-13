#version 450
layout(location = 0) in vec2 inPosition; // @vertex Position required
layout(location = 1) in vec2 inUv; // @vertex TexCoord0 required
layout(location = 2) in vec4 inColor; // @vertex Color required
layout(set = 0, binding = 1) uniform Frame { vec4 sunDirection; mat4 worldToCamera; mat4 viewProj; } frame;
layout(location = 0) out vec2 uv;
layout(location = 1) out vec4 color;
//宿主 GUI 帧块提供屏幕变换，按其行主序数据使用行向量。
void main() { gl_Position = vec4(inPosition, 0.0, 1.0) * frame.viewProj; uv = inUv; color = inColor; }
