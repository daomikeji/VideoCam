/**
 * 运行时权限处理示例
 * 在App.tsx中集成以下代码来处理摄像头权限
 */

import { Platform, PermissionsAndroid, Alert } from 'react-native';

/**
 * 请求摄像头权限
 */
export const requestCameraPermission = async (): Promise<boolean> => {
  try {
    if (Platform.OS === 'android') {
      // Android 6.0+ 需要运行时权限
      const permission = PermissionsAndroid.PERMISSIONS.CAMERA;
      const hasPermission = await PermissionsAndroid.check(permission);

      if (hasPermission) {
        return true;
      }

      const granted = await PermissionsAndroid.request(
        permission,
        {
          title: '摄像头权限请求',
          message: '应用需要访问你的摄像头来进行视频流传输',
          buttonNeutral: '稍后询问',
          buttonNegative: '取消',
          buttonPositive: '确定',
        }
      );

      return granted === PermissionsAndroid.RESULTS.GRANTED;
    } else if (Platform.OS === 'ios') {
      // iOS权限由系统自动处理
      // 首次访问摄像头时会弹出权限请求
      // 用户在Info.plist中设置的NSCameraUsageDescription
      return true;
    }

    return true;
  } catch (err) {
    console.error('Permission request error:', err);
    return false;
  }
};

/**
 * 请求麦克风权限 (可选)
 */
export const requestMicrophonePermission = async (): Promise<boolean> => {
  try {
    if (Platform.OS === 'android') {
      const permission = PermissionsAndroid.PERMISSIONS.RECORD_AUDIO;
      const hasPermission = await PermissionsAndroid.check(permission);

      if (hasPermission) {
        return true;
      }

      const granted = await PermissionsAndroid.request(
        permission,
        {
          title: '麦克风权限请求',
          message: '应用需要访问你的麦克风',
          buttonNeutral: '稍后询问',
          buttonNegative: '取消',
          buttonPositive: '确定',
        }
      );

      return granted === PermissionsAndroid.RESULTS.GRANTED;
    } else if (Platform.OS === 'ios') {
      return true;
    }

    return true;
  } catch (err) {
    console.error('Permission request error:', err);
    return false;
  }
};

/**
 * 在App.tsx中的使用示例
 */
/*
import { requestCameraPermission, requestMicrophonePermission } from './permissions';

const App = () => {
  const [hasPermission, setHasPermission] = useState(false);

  useEffect(() => {
    checkPermissions();
  }, []);

  const checkPermissions = async () => {
    const cameraOk = await requestCameraPermission();
    const micOk = await requestMicrophonePermission();

    if (cameraOk && micOk) {
      setHasPermission(true);
    } else {
      Alert.alert(
        '权限错误',
        '应用需要摄像头和麦克风权限才能正常运行',
        [{ text: '确定' }]
      );
    }
  };

  if (!hasPermission) {
    return (
      <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center' }}>
        <Text>正在请求权限...</Text>
      </View>
    );
  }

  // 应用主界面
  return <YourMainComponent />;
};
*/
