package service

import (
	"backend/internal/config"
	"backend/internal/logger"
	"fmt"

	"github.com/aliyun/aliyun-oss-go-sdk/oss"
	"go.uber.org/zap"
)

// OSSService OSS服务
type OSSService struct {
	client *oss.Client
	bucket *oss.Bucket
	cfg    config.OSSConfig
}

// NewOSSService 创建OSS服务实例
func NewOSSService() (*OSSService, error) {
	cfg := config.Get().OSS

	client, err := oss.New(cfg.Endpoint, cfg.AccessKeyID, cfg.AccessKeySecret)
	if err != nil {
		logger.Log.Error("创建OSS客户端失败", zap.Error(err))
		return nil, fmt.Errorf("创建OSS客户端失败: %w", err)
	}

	bucket, err := client.Bucket(cfg.BucketName)
	if err != nil {
		logger.Log.Error("获取OSS Bucket失败", zap.Error(err))
		return nil, fmt.Errorf("获取OSS Bucket失败: %w", err)
	}

	return &OSSService{
		client: client,
		bucket: bucket,
		cfg:    cfg,
	}, nil
}

// GetSignedURL 获取对象签名URL
// objectType: 资源类型 (live2d, voice等)
// objectName: 资源名称
func (s *OSSService) GetSignedURL(objectType, objectName string) (string, error) {
	// 构建对象路径: live2d/nori.zip
	objectKey := fmt.Sprintf("%s/%s.zip", objectType, objectName)

	// 检查对象是否存在
	exists, err := s.bucket.IsObjectExist(objectKey)
	if err != nil {
		logger.Log.Error("检查对象是否存在失败", zap.String("objectKey", objectKey), zap.Error(err))
		return "", fmt.Errorf("检查对象是否存在失败: %w", err)
	}
	if !exists {
		logger.Log.Warn("对象不存在", zap.String("objectKey", objectKey))
		return "", fmt.Errorf("资源不存在: %s", objectKey)
	}

	// 生成签名URL
	signedURL, err := s.bucket.SignURL(objectKey, oss.HTTPGet, int64(s.cfg.URLExpireSeconds))
	if err != nil {
		logger.Log.Error("生成签名URL失败", zap.String("objectKey", objectKey), zap.Error(err))
		return "", fmt.Errorf("生成签名URL失败: %w", err)
	}

	logger.Log.Info("生成签名URL成功", zap.String("objectKey", objectKey))
	return signedURL, nil
}
