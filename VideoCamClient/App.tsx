import React, { useState, useCallback, useEffect } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  SafeAreaView,
  ActivityIndicator,
  Alert,
  NativeModules,
  Platform,
  ScrollView,
} from 'react-native';

const { VideoStreamingModule } = NativeModules;

const DISCOVERY_PORT = 9005;

const App = () => {
  const [isConnected, setIsConnected] = useState(false);
  const [isStreaming, setIsStreaming] = useState(false);
  const [isFrontCamera, setIsFrontCamera] = useState(false);
  const [isFlashOn, setIsFlashOn] = useState(false);
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [discoveredIP, setDiscoveredIP] = useState<string | null>(null);
  const [tcpPort, setTcpPort] = useState(9000);
  const [udpPort, setUdpPort] = useState(9001);
  const [status, setStatus] = useState('未连接，等待发现服务器...');
  const [receivedMessages, setReceivedMessages] = useState<string[]>([]);

  const addReceivedMessage = useCallback((message: string) => {
    setReceivedMessages((prev) => [message, ...prev].slice(0, 50));
  }, []);

  /**
   * 自动发现服务
   */
  const startDiscovery = useCallback(async () => {
    if (isDiscovering || !VideoStreamingModule?.startDiscovery) return;

    setIsDiscovering(true);
    setDiscoveredIP(null);
    setStatus(`正在广播监听端口 ${DISCOVERY_PORT}，查找服务器...`);

    try {
      const result = await VideoStreamingModule.startDiscovery(DISCOVERY_PORT);

      if (result.success && result.ip) {
        setDiscoveredIP(result.ip);
        setTcpPort(result.tcp);
        setUdpPort(result.udp);
        setStatus(`发现服务器成功: ${result.ip}`);
        addReceivedMessage(`发现消息: ${result.raw ?? `IP=${result.ip}, TCP=${result.tcp}, UDP=${result.udp}`}`);
      } else {
        setStatus(`未发现服务器。请手动检查 IP。`);
        addReceivedMessage(`发现消息失败: ${result.raw ?? '无效响应'}`);
      }
    } catch (e) {
      const errorMessage = e instanceof Error ? e.message : String(e);
      setStatus(`发现过程中发生错误: ${errorMessage}`);
      addReceivedMessage(`发现错误: ${errorMessage}`);
      console.error(e);
    } finally {
      setIsDiscovering(false);
    }
  }, [isDiscovering]);

  // 组件加载时自动启动发现服务
  useEffect(() => {
    startDiscovery();
  }, [startDiscovery]);

  /**
   * 核心方法：连接、启动摄像头、编码和流传输。
   */
  const connectAndStream = useCallback(async () => {
    const targetIP = discoveredIP;

    if (!targetIP) {
      setStatus('错误：未发现服务器 IP，请先尝试发现。');
      return;
    }

    if (!VideoStreamingModule?.connectAndStream) {
      setStatus('错误：原生模块未加载。');
      return;
    }

    setStatus(`尝试连接到 ${targetIP}:${tcpPort}...`);
    try {
      const result = await VideoStreamingModule.connectAndStream(
        targetIP,
        tcpPort,
        udpPort
      );

      if (result.success) {
        setIsConnected(true);
        setIsStreaming(true);
        setStatus(`连接成功！正在通过 UDP 发送视频流...`);
        addReceivedMessage(`连接成功: ${result.message}`);
      } else {
        setStatus(`连接失败: ${result.message}`);
        addReceivedMessage(`连接失败: ${result.message}`);
      }
    } catch (e) {
      setStatus(`连接过程中发生错误: ${e instanceof Error ? e.message : String(e)}`);
      console.error(e);
    }
  }, [discoveredIP, tcpPort, udpPort]);

  /**
   * 停止流传输
   */
  const disconnectStream = useCallback(async () => {
    if (!VideoStreamingModule?.disconnect) return;

    try {
      await VideoStreamingModule.disconnect();
      setIsConnected(false);
      setIsStreaming(false);
      setIsFrontCamera(false);
      setIsFlashOn(false);
      setStatus('已断开连接。');
    } catch (e) {
      setStatus(`断开连接时发生错误: ${e instanceof Error ? e.message : String(e)}`);
    }
  }, []);

  /**
   * 切换前后摄像头
   */
  const toggleCamera = useCallback(async () => {
    if (!isConnected || !VideoStreamingModule?.sendControlCommand) return;

    const newCameraIsFront = !isFrontCamera;
    const newCameraCommand = newCameraIsFront ? 'FRONT' : 'BACK';
    const commandString = `SWITCH_CAMERA:${newCameraCommand}`;

    try {
      const success = await VideoStreamingModule.sendControlCommand(commandString);
      if (success) {
        setIsFrontCamera(newCameraIsFront);
        setStatus(`已发送命令: 切换到 ${newCameraCommand} 摄像头`);
      } else {
        setStatus('发送切换摄像头命令失败。');
      }
    } catch (e) {
      setStatus(`发送摄像头命令发生错误: ${e instanceof Error ? e.message : String(e)}`);
    }
  }, [isConnected, isFrontCamera]);

  /**
   * 开关闪光灯
   */
  const toggleFlash = useCallback(async () => {
    if (!isConnected || !VideoStreamingModule?.sendControlCommand) return;
    if (isFrontCamera) {
      setStatus("前置摄像头不支持闪光灯操作。");
      return;
    }

    const newFlashIsOn = !isFlashOn;
    const newFlashCommand = newFlashIsOn ? 'ON' : 'OFF';
    const commandString = `TOGGLE_FLASH:${newFlashCommand}`;

    try {
      const success = await VideoStreamingModule.sendControlCommand(commandString);
      if (success) {
        setIsFlashOn(newFlashIsOn);
        setStatus(`已发送命令: 闪光灯 ${newFlashCommand}`);
      } else {
        setStatus('发送开关闪光灯命令失败。');
      }
    } catch (e) {
      setStatus(`发送闪光灯命令发生错误: ${e instanceof Error ? e.message : String(e)}`);
    }
  }, [isConnected, isFlashOn, isFrontCamera]);

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.container}>
        <Text style={styles.title}>iVCam 克隆 - 移动端</Text>

        <View style={styles.messagePreviewCard}>
          <Text style={styles.messageTitle}>最近接收消息</Text>
          <ScrollView style={styles.messagePreviewScroll}>
            {receivedMessages.length === 0 ? (
              <Text style={styles.messageText}>暂无接收消息</Text>
            ) : (
              receivedMessages.slice(0, 3).map((msg, index) => (
                <Text key={`${msg}-${index}`} style={styles.messageText}>
                  {msg}
                </Text>
              ))
            )}
          </ScrollView>
        </View>

        {/* 状态显示区 */}
        <View style={styles.statusCard}>
          <Text style={styles.statusLabel}>
            当前状态:{' '}
            <Text style={[styles.statusValue, isConnected ? styles.connected : styles.disconnected]}>
              {isConnected ? '已连接' : '未连接'}
            </Text>
          </Text>
          <Text style={styles.statusMessage}>{status}</Text>
        </View>

        {/* 信息显示区 */}
        <View style={styles.infoCard}>
          <Text style={styles.infoText}>目标 IP: {discoveredIP || 'N/A'}</Text>
          <Text style={styles.infoText}>TCP/UDP 端口: {tcpPort}/{udpPort}</Text>

          <TouchableOpacity
            style={[styles.discoveryButton, isDiscovering && styles.discoveringButton]}
            onPress={startDiscovery}
            disabled={isDiscovering || isConnected}
          >
            {isDiscovering ? (
              <>
                <ActivityIndicator color="#fff" size="small" style={styles.spinner} />
                <Text style={styles.discoveryButtonText}>正在查找...</Text>
              </>
            ) : (
              <Text style={styles.discoveryButtonText}>重新发现服务器</Text>
            )}
          </TouchableOpacity>
        </View>

        {/* 连接/断开按钮 */}
        <TouchableOpacity
          style={[
            styles.connectButton,
            isConnected ? styles.disconnectButtonStyle : styles.connectButtonStyle,
            (!discoveredIP || isDiscovering) && styles.disabledButton,
          ]}
          onPress={isConnected ? disconnectStream : connectAndStream}
          disabled={!discoveredIP || isDiscovering}
        >
          <Text style={styles.connectButtonText}>
            {isConnected ? '断开连接' : '连接并开始流传输'}
          </Text>
        </TouchableOpacity>

        {/* 控制按钮 */}
        <View style={styles.controlButtonsContainer}>
          {/* 切换摄像头 */}
          <TouchableOpacity
            style={[styles.controlButton, !isConnected && styles.disabledButton]}
            onPress={toggleCamera}
            disabled={!isConnected}
          >
            <Text style={styles.controlButtonText}>📷</Text>
            <Text style={styles.controlButtonLabel}>
              {isFrontCamera ? '前置' : '后置'}
            </Text>
          </TouchableOpacity>

          {/* 开关闪光灯 */}
          <TouchableOpacity
            style={[
              styles.controlButton,
              (!isConnected || isFrontCamera) && styles.disabledButton,
              isFlashOn && styles.flashOnButton,
            ]}
            onPress={toggleFlash}
            disabled={!isConnected || isFrontCamera}
          >
            <Text style={styles.controlButtonText}>⚡</Text>
            <Text style={styles.controlButtonLabel}>
              {isFlashOn ? '关闭' : '开启'}
            </Text>
          </TouchableOpacity>
        </View>

        {/* 视频流指示器 */}
        {isStreaming && (
          <View style={styles.streamingIndicator}>
            <View style={styles.streamingDot} />
            <Text style={styles.streamingText}>视频流正在发送中...</Text>
          </View>
        )}

        <View style={styles.messageLogCard}>
          <Text style={styles.messageTitle}>消息日志</Text>
          <ScrollView style={styles.messageLogScroll}>
            {receivedMessages.length === 0 ? (
              <Text style={styles.messageText}>暂无接收消息</Text>
            ) : (
              receivedMessages.map((msg, index) => (
                <Text key={`${msg}-${index}`} style={styles.messageText}>
                  {msg}
                </Text>
              ))
            )}
          </ScrollView>
        </View>
      </View>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#f0f0f0',
  },
  container: {
    flex: 1,
    padding: 16,
    justifyContent: 'flex-start',
  },
  title: {
    fontSize: 24,
    fontWeight: 'bold',
    textAlign: 'center',
    marginBottom: 20,
    color: '#333',
  },
  statusCard: {
    backgroundColor: '#fff',
    borderRadius: 12,
    padding: 16,
    marginBottom: 16,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.1,
    shadowRadius: 4,
    elevation: 3,
  },
  statusLabel: {
    fontSize: 14,
    color: '#666',
    marginBottom: 8,
  },
  statusValue: {
    fontWeight: 'bold',
    fontSize: 16,
  },
  connected: {
    color: '#4CAF50',
  },
  disconnected: {
    color: '#f44336',
  },
  statusMessage: {
    fontSize: 12,
    color: '#999',
    minHeight: 20,
  },
  messagePreviewCard: {
    width: '100%',
    backgroundColor: '#f8f9fa',
    borderRadius: 12,
    borderWidth: 1,
    borderColor: '#e0e0e0',
    padding: 10,
    marginBottom: 12,
  },
  messageTitle: {
    fontSize: 14,
    color: '#333',
    marginBottom: 6,
    fontWeight: '600',
  },
  messagePreviewScroll: {
    maxHeight: 90,
  },
  messageLogCard: {
    width: '100%',
    backgroundColor: '#f8f9fa',
    borderRadius: 12,
    borderWidth: 1,
    borderColor: '#e0e0e0',
    padding: 10,
    marginTop: 16,
    maxHeight: 180,
  },
  messageLogScroll: {
    maxHeight: 160,
  },
  messageText: {
    color: '#444',
    fontSize: 13,
    lineHeight: 18,
    marginBottom: 4,
  },
  infoCard: {
    backgroundColor: '#E3F2FD',
    borderRadius: 12,
    padding: 16,
    marginBottom: 16,
    borderLeftWidth: 4,
    borderLeftColor: '#1976D2',
  },
  infoText: {
    fontSize: 13,
    color: '#1565C0',
    marginBottom: 8,
  },
  discoveryButton: {
    backgroundColor: '#1976D2',
    borderRadius: 8,
    padding: 12,
    marginTop: 12,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
  },
  discoveringButton: {
    backgroundColor: '#FFA726',
  },
  discoveryButtonText: {
    color: '#fff',
    fontWeight: '600',
    fontSize: 13,
    marginLeft: 8,
  },
  spinner: {
    marginRight: 4,
  },
  connectButton: {
    borderRadius: 12,
    padding: 16,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 16,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.15,
    shadowRadius: 4,
    elevation: 4,
  },
  connectButtonStyle: {
    backgroundColor: '#4CAF50',
  },
  disconnectButtonStyle: {
    backgroundColor: '#f44336',
  },
  connectButtonText: {
    color: '#fff',
    fontWeight: 'bold',
    fontSize: 16,
  },
  disabledButton: {
    opacity: 0.5,
  },
  controlButtonsContainer: {
    flexDirection: 'row',
    gap: 12,
    marginBottom: 16,
  },
  controlButton: {
    flex: 1,
    backgroundColor: '#666',
    borderRadius: 12,
    padding: 16,
    alignItems: 'center',
    justifyContent: 'center',
  },
  flashOnButton: {
    backgroundColor: '#FFC107',
  },
  controlButtonText: {
    fontSize: 32,
    marginBottom: 8,
  },
  controlButtonLabel: {
    color: '#fff',
    fontSize: 12,
    fontWeight: '600',
  },
  streamingIndicator: {
    backgroundColor: '#C8E6C9',
    borderRadius: 12,
    padding: 12,
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
    borderColor: '#4CAF50',
  },
  streamingDot: {
    width: 12,
    height: 12,
    borderRadius: 6,
    backgroundColor: '#4CAF50',
    marginRight: 12,
  },
  streamingText: {
    color: '#2E7D32',
    fontWeight: '600',
    fontSize: 14,
  },
});

export default App;